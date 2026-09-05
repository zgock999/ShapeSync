# ShapeSync API Reference Build Manual

This manual explains how to generate the ShapeSync API reference with DocFX.
Generated metadata and local HTML output are temporary build products and must
not be committed to the package repository's `main` branch. The reviewed site
is copied to the public `gh-pages` tree under `/api/` and committed there as a
release asset.

## Scope

- The Core reference contains `zgock.ShapeSync.Runtime` and
  `zgock.ShapeSync.Editor`.
- The VRM companion reference contains only
  `zgock.ShapeSync.VrmIntegration.Runtime` and
  `zgock.ShapeSync.VrmIntegration.Editor`.
- Test assemblies, PlayTest data, fixtures, and generated Human Test output are
  not part of either reference.

## Prerequisites

1. Open the ShapeSync project once in Unity so that the four `*.csproj` files
   are current.
2. Install a supported .NET SDK. The repository pins DocFX in
   `Docs/Docfx/.config/dotnet-tools.json`; do not install an arbitrary global
   DocFX version for this workflow.
3. Run all commands below from `Docs/Docfx`.

```powershell
Set-Location Docs/Docfx
dotnet tool restore
```

## Build the Core reference

The Core reference must build with the release-default Core-only configuration.
It does not require UniVRM or `SHAPESYNC_USE_UNIVRM`.

```powershell
dotnet tool run docfx metadata docfx.json --logLevel error
dotnet tool run docfx build docfx.json --warningsAsErrors
./Sanitize-PublicSite.ps1 -SiteRoot _site
```

Open `_site/index.html` after a successful build.

## Build the optional VRM companion reference

Before generating this reference, open the project with UniVRM installed and
`SHAPESYNC_USE_UNIVRM` enabled, then let Unity regenerate the project files.
The VRM reference is intentionally separate from Core.

```powershell
dotnet tool run docfx metadata docfx.vrm.json --logLevel error
dotnet tool run docfx build docfx.vrm.json --warningsAsErrors
./Sanitize-PublicSite.ps1 -SiteRoot _site-vrm
```

Open `_site-vrm/vrm/index.html` after a successful build.

## Publish the generated reference

Build the Core and optional VRM sites separately, then assemble the public API
staging directory. Keep the two generated sites distinct so users can choose
the Core reference or the VRM companion reference:

```text
<pages-stage>/api/
  core/   contents of Docs/Docfx/_site/
  vrm/    contents of Docs/Docfx/_site-vrm/ with its generated vrm/*
          landing files promoted to the public vrm/ root
```

The VRM DocFX source keeps its landing page under `vrm/` so that the local
generated site can be built independently. The public layout must flatten
that one generated landing-page directory: publish the contents of
`_site-vrm/vrm/` as `/api/vrm/`, while keeping `_site-vrm/api/` and
`_site-vrm/public/` below `/api/vrm/`. Run
`Tools/Spec23/Flatten-VrmPublicSite.ps1` after copying the generated site.
The public landing page is therefore `/api/vrm/`; do not publish the landing
page only at `/api/vrm/vrm/`.

Copy the generated output only into the `gh-pages` worktree. Do not copy
`Docs/Docfx/obj/`, `_site/`, or `_site-vrm/` into package `main`, and do not
place the generated site under the package assembly's `Packages/` tree. After
the staging tree passes the link and HTTP checks, continue with the publish
step below.

Before copying either generated site, run the sanitization command shown above.
It removes DocFX contribution metadata and edit links, then fails if a private
Azure DevOps host remains in generated HTML. After the staging tree passes the
link, external-URL inventory, and HTTP checks, commit the generated API files
on `gh-pages` without rewriting published history.

## Clean rebuild

Use this only from `Docs/Docfx`. These are generated, Git-ignored directories.
Cleaning is useful after changing DocFX source-project inputs or when a removed
API still appears in a generated TOC.

```powershell
Remove-Item -LiteralPath obj/api, _site, obj/vrm-api, _site-vrm -Recurse -Force -ErrorAction SilentlyContinue
```

Then rerun the relevant metadata and build commands above.

## Acceptance checks

- Both commands must finish with `0 warning(s)` and `0 error(s)`.
- Both generated sites must pass `Sanitize-PublicSite.ps1`; private source URLs
  must not be present in the HTML output.
- Core TOC must contain only `zgock.ShapeSync` and `zgock.ShapeSync.Editor`.
- VRM TOC must contain only `zgock.ShapeSync.VrmIntegration` and its `.Editor`
  namespace.
- Confirm that every new or modified public API has an accurate English XML
  documentation comment. This is a project rule in `AGENT.md`.
- Inherited UnityEngine and UnityEditor member lists are intentionally hidden.
  The reference documents ShapeSync-declared APIs and avoids misleading links
  caused by an unresolved Unity API xref map.

## Troubleshooting

- If a `*.csproj` file or optional UniVRM reference is missing, return to Unity,
  correct the project configuration, and allow Unity to regenerate the files.
- If `dotnet tool run docfx` cannot find the tool, rerun `dotnet tool restore`.
- Do not edit `obj/`, `_site/`, or `_site-vrm/`; edit source comments or the
  configuration files in `Docs/Docfx` instead.
