using System;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using TerraKit.Editor;
using UnityEngine;

namespace TerraKit.Tests
{
    public sealed class ResourceIntegrationTests
    {
        private TerraKitGraphAsset _graph;
        [SetUp] public void SetUp()
        {
            string error;
            Assert.IsTrue(TerraKitNodeRegistry.RefreshBackend(out error), error);
            _graph = ScriptableObject.CreateInstance<TerraKitGraphAsset>();
        }
        [TearDown] public void TearDown() { UnityEngine.Object.DestroyImmediate(_graph); }

        private TerraKitNodeData Add(string type)
        {
            ITerraKitNodeDefinition definition;
            Assert.IsTrue(TerraKitNodeRegistry.TryGet(type, out definition));
            var node = TerraKitGraphEditorUtil.CreateNodeData(definition, Vector2.zero);
            _graph.nodes.Add(node); return node;
        }
        private void Connect(TerraKitNodeData from, string output, TerraKitNodeData to, string input)
        {
            _graph.edges.Add(new TerraKitEdgeData { id = Guid.NewGuid().ToString(), outputNodeId = from.id,
                outputPortId = output, inputNodeId = to.id, inputPortId = input });
        }
        private TerraKitNodeData HeightGraph(bool noise = false)
        {
            var flat = Add("terrakit.height.flat");
            flat.parameters.First(p => p.key == "elevation").value = "7.5";
            if (!noise) return flat;
            var next = Add("terrakit.height.noise"); Connect(flat, "height", next, "source"); return next;
        }
        private void Mesh(TerraKitNodeData height)
        { var mesh = Add("terrakit.mesh.height_field"); Connect(height, "height", mesh, "height"); }

        [Test] public void AbiSizesAndOffsetsMatchNativeHeader()
        {
            TerraKitNativeAbiLayout.Validate(); TerraKitBackendExecutionAbiLayout.Validate();
            Assert.AreEqual(112, Marshal.OffsetOf(typeof(TerraKitNativeHeightFieldView), "Values").ToInt32());
            Assert.AreEqual(120, Marshal.OffsetOf(typeof(TerraKitNativeVolumeView), "Values").ToInt32());
            Assert.AreEqual(16, Marshal.OffsetOf(typeof(TerraKitNativeRegionLayout3D), "BaseSpacing").ToInt32());
        }
        [Test] public void HeightOnlyGraphGeneratesWithoutMeshAndOwnsItsSamples()
        {
            HeightGraph();
            var result = new TerraKitBackendGraphRunner().GenerateResources(_graph, TerraKitBackendGenerationRequest.Default);
            Assert.AreEqual(1, result.Resources.Count);
            var grid = result.Resources[0].Grid;
            Assert.AreEqual(17, grid.Width); Assert.AreEqual(17, grid.Height); Assert.AreEqual(289, grid.Count);
            Assert.IsTrue(grid.Values.All(v => v == 7.5f));
            GC.Collect(); Assert.AreEqual(7.5f, grid.Values[288]);
            Assert.AreEqual(1, grid.Sampling); Assert.AreEqual(1, grid.HeightAxis.Y);
        }
        [Test] public void MultipleMeshesAndIntermediateHeightsAreReturned()
        {
            var height = HeightGraph(true); Mesh(height); Mesh(height);
            var result = new TerraKitBackendGraphRunner().GenerateResources(_graph, TerraKitBackendGenerationRequest.Default);
            Assert.AreEqual(4, result.Resources.Count);
            Assert.AreEqual(2, result.Resources.Count(r => r.Mesh != null));
            foreach (var mesh in result.Resources.Where(r => r.Mesh != null).Select(r => r.Mesh))
            { Assert.AreEqual(289, mesh.Positions.Length); Assert.AreEqual(1536, mesh.Indices.Length); }
        }
        [Test] public void RepeatGenerationIsDeterministicAndLodResolves()
        {
            HeightGraph(true);
            var request = new TerraKitBackendGenerationRequest(1234, -2, 3, 1, 16, 16, 1, 1);
            var runner = new TerraKitBackendGraphRunner();
            var a = runner.GenerateResources(_graph, request).Resources.Last().Grid;
            var b = runner.GenerateResources(_graph, request).Resources.Last().Grid;
            CollectionAssert.AreEqual(a.Values, b.Values); Assert.AreEqual(9, a.Width);
            Assert.AreEqual(-32, a.Transform.Origin.X); Assert.AreEqual(48, a.Transform.Origin.Z);
        }
        [Test] public void AdjacentRegionsShareHeightBoundary()
        {
            HeightGraph(true);
            var runner = new TerraKitBackendGraphRunner();
            var results = runner.GenerateResources(_graph, new[] {
                new TerraKitBackendGenerationRequest(1234, 0, 0, 0, 16, 16, 1, 1),
                new TerraKitBackendGenerationRequest(1234, 1, 0, 0, 16, 16, 1, 1) });
            var a = results[0].Resources.Last().Grid; var b = results[1].Resources.Last().Grid;
            for (int y = 0; y < 17; y++) Assert.AreEqual(a.Values[y * 17 + 16], b.Values[y * 17]);
        }
        [Test] public void InvalidGraphConnectionsAndCyclesAreRejected()
        {
            var flat = HeightGraph(); var noise = Add("terrakit.height.noise");
            Connect(flat, "height", noise, "source"); Connect(flat, "height", noise, "source");
            Assert.IsFalse(new TerraKitGraphCompiler().Compile(_graph).Success);
            _graph.edges.Clear(); Connect(noise, "height", noise, "source");
            StringAssert.Contains("cycle", string.Join(" ", new TerraKitGraphCompiler().Compile(_graph).Errors));
        }
        [Test] public void SchemaVersionMismatchIsReported()
        {
            var node = HeightGraph(); node.schemaVersion = 999;
            StringAssert.Contains("schema", string.Join(" ", new TerraKitGraphCompiler().Compile(_graph).Errors));
        }
        [Test] public void HeightStageRejects3DWithBackendDiagnostic()
        {
            HeightGraph();
            var request = new TerraKitBackendGenerationRequest(1, 0, 0, 0, 2, 2, 1, 1, true, 0, 2, 1);
            var error = Assert.Throws<TerraKitNativeException>(() => new TerraKitBackendGraphRunner().GenerateResources(_graph, request));
            StringAssert.Contains("2D", error.Message);
        }
        [Test] public void Native3DRuntimeExecutesAnEmptyPipeline()
        {
            // There is no production 3D stage yet. Exercise the real ABI without inventing one.
            IntPtr registry = IntPtr.Zero, assembler = IntPtr.Zero, pipeline = IntPtr.Zero, runtime = IntPtr.Zero, result = IntPtr.Zero;
            try
            {
                Assert.AreEqual(0, TerraKitNativeRegistryMethods.CreateBuiltInRegistry(out registry));
                Assert.AreEqual(0, TerraKitNativePipelineMethods.CreateAssembler(registry, out assembler));
                Assert.AreEqual(0, TerraKitNativePipelineMethods.FinishAssembler(ref assembler, out pipeline));
                var layout = new TerraKitNativeRegionLayout3D { CellWidth = 4, CellHeight = 6, CellDepth = 8,
                    BaseSpacing = new TerraKitNativeVector3Float64 { X = 1, Y = 2, Z = 3 } };
                Assert.AreEqual(0, TerraKitNativeRuntimeMethods.Create3D(ref pipeline, ref layout, out runtime));
                var request = new TerraKitNativeGenerationRequest3D { Seed = ulong.MaxValue, RegionX = -1, RegionY = 2, RegionZ = -3, LodLevel = 1 };
                Assert.AreEqual(0, TerraKitNativeRuntimeMethods.Generate3D(runtime, ref request, out result));
                Assert.AreNotEqual(IntPtr.Zero, result);
            }
            finally
            {
                if (result != IntPtr.Zero) TerraKitNativeResultMethods.Destroy(ref result);
                if (runtime != IntPtr.Zero) TerraKitNativeRuntimeMethods.Destroy(ref runtime);
                if (pipeline != IntPtr.Zero) TerraKitNativePipelineMethods.DestroyPipeline(ref pipeline);
                if (assembler != IntPtr.Zero) TerraKitNativePipelineMethods.DestroyAssembler(ref assembler);
                if (registry != IntPtr.Zero) TerraKitNativeRegistryMethods.DestroyRegistry(ref registry);
            }
        }
        [Test] public void DensityCopyAndSlicePreserveXYZOrder()
        {
            var values = new float[] { -3, -2, -1, 0, 1, 2, 3, 4 };
            IntPtr pointer = Marshal.AllocHGlobal(32);
            TerraKitGridData grid;
            try
            {
                Marshal.Copy(values, 0, pointer, values.Length);
                grid = TerraKitResourceReader.CopyVolume(new TerraKitNativeVolumeView
                { Width = 2, Height = 2, Depth = 2, Sampling = 1, Values = pointer, ValueCount = new UIntPtr(8) }, TerraKitBackendResourceKind.DensityField);
            }
            finally { Marshal.FreeHGlobal(pointer); }
            CollectionAssert.AreEqual(values, grid.Values);
            var texture = TerraKitResourceExport.Preview(grid, 1, -3, 4);
            try { Assert.Greater(texture.GetPixel(1, 1).r, texture.GetPixel(0, 0).r); }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
        [Test] public void VoxelIdsRetainFullUnsigned32BitRangeInCopyAndJson()
        {
            IntPtr pointer = Marshal.AllocHGlobal(8);
            TerraKitGridData grid;
            try
            {
                Marshal.WriteInt32(pointer, 0, 0); Marshal.WriteInt32(pointer, 4, -1);
                grid = TerraKitResourceReader.CopyVolume(new TerraKitNativeVolumeView
                { Width = 2, Height = 1, Depth = 1, Sampling = 2, Values = pointer, ValueCount = new UIntPtr(2) }, TerraKitBackendResourceKind.VoxelVolume);
            }
            finally { Marshal.FreeHGlobal(pointer); }
            Assert.AreEqual(uint.MaxValue, grid.VoxelIds[1]);
            var resource = new TerraKitGeneratedResource(new TerraKitOutputDescriptor("test", "Fixture", "voxels", "Voxels", TerraKitBackendResourceKind.VoxelVolume, 1), grid);
            string json = TerraKitResourceExport.ToJson(resource, TerraKitBackendGenerationRequest.Default, "fixture", "{}", "now");
            StringAssert.Contains("4294967295", json);
        }
        [Test] public void MalformedBufferIsRejectedBeforeReading()
        {
            Assert.Throws<InvalidOperationException>(() => TerraKitResourceReader.CopyHeight(new TerraKitNativeHeightFieldView
            { Width = 2, Height = 2, Sampling = 1, Values = IntPtr.Zero, ValueCount = new UIntPtr(4) }));
        }
        [Test] public void JsonExportContainsExactRequestAndOriginalHeightSamples()
        {
            HeightGraph();
            var request = new TerraKitBackendGenerationRequest(ulong.MaxValue, -4, 6, 0, 2, 2, 0.25, 0.5);
            var result = new TerraKitBackendGraphRunner().GenerateResources(_graph, request);
            string json = TerraKitResourceExport.ToJson(result.Resources[0], request, "height", "{}", "now");
            StringAssert.Contains("18446744073709551615", json);
            StringAssert.Contains("7.5", json); StringAssert.Contains("HeightField", json);
            StringAssert.Contains("HeightAxis", json); StringAssert.Contains("0.25", json);
        }
        [Test] public void LegacyMeshShortcutStillWorks()
        {
            Mesh(HeightGraph(true));
            var mesh = new TerraKitBackendGraphRunner().Generate(_graph);
            Assert.AreEqual(289, mesh.Positions.Length);
        }
    }
}