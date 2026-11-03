# Publish the VS Code extension preview

The extension identity is `kidoz.dotprolog`. Its version is independent of the .NET packages:
`src/DotProlog.VSCode/package.json` currently declares `0.1.0`.

## Configure Trusted Publishing once

Register the `kidoz` publisher in [Visual Studio Marketplace publisher management](https://marketplace.visualstudio.com/manage).
Open that publisher's **Trusted Publishing** page and create this policy:

| Field | Value |
| --- | --- |
| Policy name | DotProlog VS Code preview |
| Repository owner | `kidoz` |
| Repository | `dotprolog` |
| Workflow | `vscode-publish.yml` |
| Environment | `vscode-marketplace` |

Create the matching GitHub environment `vscode-marketplace` in the repository settings. Restrict
deployments to `main`; configure required reviewers if an approval gate is wanted. The publisher
registration and policy are separate steps. No PAT or repository publishing secret is needed.
These fields follow Microsoft's [Trusted Publishing instructions](https://code.visualstudio.com/api/working-with-extensions/publishing-extension#publish-with-github-actions-and-trusted-publishing).

## Prepare a reviewable release

1. Update the extension version and its `CHANGELOG.md`, keeping `preview: true` for this channel.
2. Commit the extension sources, images and workflows to `main`. README image URLs resolve from
   that branch, so the assets must be present there before publication.
3. Run **VS Code Marketplace preview** (`vscode-publish.yml`) with the exact extension version
   and leave **publish** disabled. Alternatively:

   ```sh
   gh workflow run vscode-publish.yml --ref main -f version=0.1.0 -F publish=false
   ```

The workflow calls all 12 managed/native editor checks across Linux x64, Windows x64 and macOS
arm64, with both VS Code 1.91.0 and current stable. It collects the three native packages plus the
universal managed fallback, verifies identity, target, pre-release metadata, PNG icon, listing
screenshots and server payloads, and writes `SHA256SUMS`.

Download the `dotprolog-marketplace-<version>` artifact and review the four VSIX files. The native
packages need no .NET runtime for loose files. The managed fallback requires .NET 10; project mode
always requires the .NET 10 SDK. Other native architectures are not part of this release.

## Publish the reviewed version

After reviewing the artifact and authorizing publication, run the same workflow from `main` with
**publish** enabled. It repeats validation, verifies the collected checksums, then uses the
environment-scoped OIDC identity with `vsce publish --pre-release --oidc`. Only that final job has
`id-token: write` permission.

Check the workflow result and the [Marketplace listing](https://marketplace.visualstudio.com/items?itemName=kidoz.dotprolog).
Marketplace processing can continue after upload. Confirm all target packages appear and install
the pre-release from Marketplace on each supported OS before announcing availability.

The extension is marked **Preview**, and packages also opt into VS Code's **pre-release** update
channel. Keep numeric `major.minor.patch` versions increasing; do not add a `-preview` suffix.
The workflow does not tag the repository, publish NuGet packages or change the core release flow.

## Maintain the visual assets

`src/DotProlog.VSCode/images/` contains editable `icon.svg` and `banner.svg` masters. The icon
continues the repository's navy, purple and cyan identity. Export PNGs with librsvg:

```sh
cd src/DotProlog.VSCode/images
rsvg-convert -w 256 -h 256 icon.svg -o icon.png
rsvg-convert -w 512 -h 512 icon.svg -o icon-512.png
rsvg-convert banner.svg -o banner.png
```

The VSIX includes PNG assets only. Refresh `editor.png` and `diagnostics.png` from the actual
extension UI when features change. Keep sample workspaces free of account details and private code.
