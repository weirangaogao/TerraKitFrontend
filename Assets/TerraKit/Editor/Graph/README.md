# TerraKit Unity Frontend

TerraKit Unity Frontend provides a Unity Graph Editor and runtime integration for generating terrain through the TerraKit native backend.

## Requirements

- Unity `6000.5.6f1`
- Universal Render Pipeline (URP)
- TerraKit native backend library

Support for additional platforms can be added by providing compatible native libraries.

## Native Backend Library

The Unity frontend communicates with the TerraKit backend through a native C API library.

Pre-built native libraries are available from the TerraKit GitHub Releases page.

For local Unity integration, the required native library should be placed under:

```text
Assets/Plugins/TerraKit/
```

This project currently includes an Apple Silicon macOS native library under `macOS/`.
For Windows, obtain the matching x86_64 Windows release and place `terrakit.dll` under `Assets/Plugins/TerraKit/Windows/`. Configure its Unity plugin import settings for Windows Editor and Windows x86_64 only. Verify the ABI and test generation on Windows before claiming support.
The currently checked release is library `0.0.2` with ABI `0.1.0`. Library and ABI versions are different version numbers.

## Quick Start

### 1. Check the Backend

Open the Unity project and select:

```text
Tools > TerraKit > Check Backend Connection
```

A successful check displays the ABI version, library version, and three built-in backend stages.

### 2. Create or Open a Backend Graph

Open:

Tools > TerraKit > Graph Editor

Open an existing backend-compatible graph or create a new one.

A backend-compatible graph should contain executable backend stages, such as:

Flat Height
    -> Noise Height
    -> Heightfield Mesh

Use Validate to check graph structure and parameter values, locate node errors, and inspect the execution order without generating terrain.

The current Unity mesh generator requires exactly one Mesh output port in the graph. Zero or multiple Mesh outputs fail validation. This is a limit of the current Unity generator, not of the backend ABI.

The Generator checks the selected graph before enabling its Generate buttons. Manual validation is optional: Generate checks again before execution, because the graph may have changed. Native execution can still report additional errors.

### 3. Generate Terrain

Open:

```text
Tools > TerraKit > Backend Generator
```

Choose the graph and start with:

| Setting | Value |
| --- | ---: |
| Seed | `12345` |
| Region X / Y | `0 / 0` |
| LOD | `0` |
| Cell Width / Height | `16 / 16` |
| Base Spacing | `1 / 1` |

Use:

- **Generate Single Region Preview** for one terrain region.
- **Generate 3 x 3 Region Preview** to inspect neighbouring regions and seams.

Changing the Seed changes the terrain shape, but does not change the estimated vertex or triangle
count.

### 4. Save Output

After generating terrain, use:

- **Save Single Mesh Asset**
- **Save 3 x 3 Meshes + Prefab**

The default output folder is:

```text
Assets/TerraKitGenerated
```

## Troubleshooting

### Backend connection fails

Confirm that a compatible TerraKit native backend library exists under:

```text
Assets/Plugins/TerraKit/
```

Then run **Check Backend Connection** again.

### Generate buttons are disabled

Confirm that:

- A valid graph with exactly one Mesh output port is selected and the backend registry is
available.
- Seed is a valid whole number.
- LOD is between `0` and `30`.
- Cell dimensions are between `1` and `4096`.
- Base Spacing values are greater than zero.

### Validate passes but generation fails

Validate checks graph structure and schema-defined parameter rules. Native execution can report
additional errors; read the generation error details.

Only nodes discovered from the backend registry are supported. The retired `terra.*` prototype
nodes are no longer supported. Use **Backend Demo** to create a working example.

## Editing

Copying multiple nodes also copies connections whose two endpoints are in the copied selection.
Pasted nodes and connections receive new IDs and do not connect back to the original nodes.

Use Unity Undo and Redo to restore graph edits. The graph view and parameter inspector reload after
each operation. Manual Validate is optional before generation.

Changing graph parameters or connections clears the previous validation report and node error
badges. Run Validate again to check the updated graph.