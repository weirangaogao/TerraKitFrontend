# TerraKit Unity Frontend

TerraKit provides a Unity Graph Editor and a Generator backed by the native TerraKit C API. Configure a graph, generate a finite map, inspect its results, and save the map as a Prefab with Mesh and material assets.

## Requirements

- Unity 6000.5.6f1.
- A compatible TerraKit native library under `Assets/Plugins/TerraKit/`, with import settings matching the Editor platform and architecture.
- A render pipeline compatible with the materials used for the map.

Run **Tools > TerraKit > Check Backend Connection** to inspect the loaded library, ABI, and available stages. Node availability comes from the backend registry. Supporting a resource type in the frontend does not create a backend stage that generates it.

## Create a graph

Open **Tools > TerraKit > Graph Editor**, then use **New Graph**, select an existing graph, or use **Demo Graph**.

A basic height pipeline is:

```text
Flat Height -> Noise Height -> Heightfield Mesh
```

Right-click the canvas to create nodes. Select a node to edit its parameters in the Inspector. The **Controls** button on the canvas expands or collapses the shortcut reference. Use **Frame All**, **Focus**, or **100%** to adjust the view.

**Validate** checks graph structure, parameter values, and execution order. Errors can identify the affected nodes. Graph edits clear the previous validation report; run Validate again when needed. Generation also validates the graph before native execution.

A graph can return intermediate HeightField outputs and multiple Mesh outputs. There is no requirement to have exactly one Mesh output. DensityField and VoxelVolume generation requires compatible stages in the loaded backend.

## Generate a map

Click **Generator** in the Graph Editor toolbar. Start with:

| Setting | Value |
| --- | --- |
| Region mode | 2D |
| Seed | 12345 |
| LOD | 0 |
| Cell Width / Height | 16 / 16 |
| Base Spacing X / Y | 1 / 1 |
| Regions X / Y | 3 / 3 |

Click **Generate Map**. Region counts determine how many neighboring regions are requested through the same pipeline. Coordinates are assigned automatically around zero; users do not enter a world location. Even counts have one more region on the negative side. In 2D mode, the second region axis corresponds to Unity world Z.

3D mode adds depth and a third region axis. The graph must contain stages compatible with 3D generation.

Changing the graph's generation content or request settings invalidates the previous results and clears the temporary scene preview. Moving a graph node does not change the terrain recipe.

## Inspect, preview, and save

The **Region** and **Output** selectors choose the result shown in the data panel. They also select the result exported as JSON or a data image.

When a Mesh output is selected:

- **Show Map Preview** displays all generated Mesh outputs from all regions in the Scene.
- **Save Mesh** saves every Mesh output in the selected Region, including its materials, as a Region Prefab and associated resource assets. The Output selector does not limit this save. This action does not place an instance in the Scene.
- **Save Prefab** saves every Mesh output from all generated regions, with materials, as one map Prefab and places an instance at the Unity origin.

Both save operations preserve Mesh-to-material assignments. Temporary textures used by those materials are saved as assets; existing texture assets remain referenced. Materials using a RenderTexture must be changed to use a persistent texture before saving.

The generated map layout is shared by Preview and the map Prefab. The Preview root and the automatically placed map Prefab instance start at Unity position `(0, 0, 0)`. Child objects retain their relative region positions. Manually dragging a saved Prefab into a Scene can place that new instance at the drop location.

HeightField, DensityField, and VoxelVolume selections do not show the Save section.

## Export

| Selected output | JSON | Image |
| --- | --- | --- |
| HeightField | Original height samples and metadata | **Export Heightmap**: grayscale heightmap PNG |
| DensityField | Complete 3D density samples and metadata | **Export Slice Image**: current Z slice PNG |
| VoxelVolume | Complete voxel data and metadata | **Export Slice Image**: current Z slice PNG |
| Mesh | Selected Mesh result and metadata | **Export PNG**: rendered image of the complete map |

Images are visualizations. Use JSON when exact original values or complete volume data are needed. Mesh PNG export includes all map Meshes and does not require an existing scene preview.

## Troubleshooting

### Backend connection fails

Check the native library path, platform import settings, architecture, and ABI compatibility. Run **Check Backend Connection** again after fixing the library.

### Generation settings are invalid

Read the Generator's field messages. Seed must fit an unsigned 64-bit integer, LOD must be between 0 and 30, cell dimensions must be between 1 and 4096, and spacing must be finite and greater than zero. Cell dimensions must be divisible by `2^LOD`. Region counts must be positive integers. Large valid requests can still exceed available resources.

### Validation passes but generation fails

Native stages may impose additional constraints. Read the backend diagnostic, particularly when using 2D stages with a 3D request.

### A node type is missing

The Create Node menu lists stages discovered from the loaded backend. A DensityField or VoxelVolume reader in the frontend does not imply that the backend contains a corresponding producer or meshing stage.

## Editing and runtime API

Copying multiple nodes includes connections whose two endpoints are in the selection. Pasted nodes and edges receive new IDs. Use Unity Undo and Redo to restore graph edits.

Runtime callers use `TerraKitBackendGraphRunner.GenerateResources(...)` with one request or a list of compatible region requests, then inspect the returned resources. The legacy `Generate(...)` shortcut has been removed.

Compilation issues are stored in `TerraKitGraphCompileResult.ErrorIssues`. Each issue contains a message and an optional Node ID. `Success` is derived from that list; separate `Errors` and `Warnings` lists are no longer exposed.