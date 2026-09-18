# TerraKit Frontend

This repository contains the Unity frontend for TerraKit.

## Unity Version

**Unity 6000.5.6f1**

Using the same Unity version is recommended to avoid compatibility issues.

---

## Running on Windows

### 1. Download the Project

Clone this repository or download it as a ZIP file from GitHub.

If you download the ZIP file, extract it first.

---

### 2. Add the Windows Backend Library

To run TerraKit on Windows, you need the Windows backend library:

```text
terrakit.dll
```

Place `terrakit.dll` in this folder inside the Unity project:

```text
Assets/Plugins/TerraKit/Windows/terrakit.dll
```

The folder structure should look like this:

```text
Assets/
└── Plugins/
    └── TerraKit/
        └── Windows/
            └── terrakit.dll
```

If the `Windows` folder does not exist, create it first.

> Important: Do not use the macOS `.dylib` file on Windows. Windows requires `terrakit.dll`.

---

## Check the Backend Connection

After opening the project in Unity:

1. Wait for Unity to finish importing and compiling.
2. Go to:

```text
Tools > TerraKit > Check Backend Connection
```

3. Check that the TerraKit backend is detected successfully.

If the backend is not detected, make sure `terrakit.dll` is located at:

```text
Assets/Plugins/TerraKit/Windows/terrakit.dll
```

---

## Open the TerraKit Graph Editor

In Unity, go to:

```text
Tools > TerraKit > Graph Editor
```

You can then create or select a TerraKit generation graph and use the frontend.
