# ShapeSync Spec22.3 TestProject

This is the small Unity project used for the package-repository Slim Test lane.
It contains no `PlayTest` reference or asset. The default scripting define set is
empty; the VRM lane explicitly adds `SHAPESYNC_USE_UNIVRM` and the final lane
removes it again.

## Open and restore

Use Unity `6000.3.18f1` and open `TestProject/`. From the repository root, restore
the R3 core closure before opening Unity:

```powershell
nugetforunity restore .\TestProject
```

The committed `TestProject/Assets/packages.config` is the restore input. The
restored `TestProject/Assets/Packages/` directory is generated and must remain
untracked.

The manifest deliberately references both packages with:

```text
file:../../Packages/net.zgock-lab.shapesync
file:../../Packages/net.zgock-lab.shapesync.vrm
```

In the pure package-repository clone these directories are siblings of
`TestProject/`. In this development Sandbox, use
`Tools/Spec22/Run-SlimTestMatrix.ps1`, which materializes the current release
tree into an ignored stage before opening Unity.

## Lanes

The matrix is executed in this order:

1. Core-only: empty defines; Core EditMode and PlayMode assemblies.
2. VRM: `SHAPESYNC_USE_UNIVRM`; Core and VRM companion EditMode and PlayMode
   assemblies.
3. Core-only return: empty defines; the same Core assemblies as lane 1.

The runner writes Unity logs, XML results, the resolved `packages-lock.json`, and
the lane summary under `Temp/Spec22/Spec22.3/`. None of those generated files are
release or source artifacts.
