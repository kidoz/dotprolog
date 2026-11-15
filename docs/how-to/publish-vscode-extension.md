# Publish the VS Code extension

The extension identity is `kidoz.dotprolog`. Its version is independent of the .NET packages:
`src/DotProlog.VSCode/package.json` currently declares `1.0.0`.

## Configure Trusted Publishing once

Register the `kidoz` publisher in [Visual Studio Marketplace publisher management](https://marketplace.visualstudio.com/manage).
Open that publisher's **Trusted Publishing** page and create this policy:

| Field | Value |
| --- | --- |
| Policy name | DotProlog VS Code release |
| Repository owner | `kidoz` |
| Repository | `dotprolog` |
| Workflow | `vscode-publish.yml` |
| Environment | `vscode-marketplace` |

Create the matching GitHub environment `vscode-marketplace` in the repository settings. Restrict
deployments to `main`; configure required reviewers if an approval gate is wanted. The publisher
registration and policy are separate steps. No PAT or repository publishing secret is needed.
These fields follow Microsoft's [Trusted Publishing instructions](https://code.visualstudio.com/api/working-with-extensions/publishing-extension#publish-with-github-actions-and-trusted-publishing).

## Prepare a reviewable release

1. Update the extension version in `package.json` and add the matching entry to its `CHANGELOG.md`.
   Do not set `preview`; stable packages must not carry the preview listing flag or the VS Code
   pre-release channel metadata.
2. Commit the extension sources, images and workflows to `main`. README image URLs resolve from
   that branch, so the assets must be present there before publication.
3. Run **VS Code Marketplace release** (`vscode-publish.yml`) with the exact extension version
   and leave **publish** disabled. Alternatively:

   ```sh
   gh workflow run vscode-publish.yml --ref main -f version=1.0.0 -F publish=false
   ```

The workflow calls all 12 managed/native editor checks across Linux x64, Windows x64 and macOS
arm64, with both VS Code 1.91.0 and current stable. It collects the three native packages plus the
universal managed fallback, verifies the stable identity, target, absence of preview and pre-release
metadata, PNG icon, listing screenshots and server payloads, and writes `SHA256SUMS`.

Download the `dotprolog-marketplace-<version>` artifact and review the four VSIX files. The native
packages need no .NET runtime for loose files. The managed fallback requires .NET 10; project mode
always requires the .NET 10 SDK. Other native architectures are not part of this release.

Before publication, complete the release checklist:

- [ ] All 12 managed/native editor jobs passed for the release commit on `main`.
- [ ] The four VSIX files were reviewed from the collected artifact and their `SHA256SUMS` match.
- [ ] A real editor verified highlighting, diagnostics, hover, Outline, completion, definitions,
      references and rename on the supported platforms.
- [ ] The selected-program path verified project mode and Restricted Mode behavior.
- [ ] At least one remote workspace (SSH, WSL or a container) was exercised if remote support is
      part of the announcement.
- [ ] The Marketplace listing assets and screenshots match the release UI.

## Publish the reviewed version

After reviewing the artifact and authorizing publication, run the same workflow from `main` with
**publish** enabled. It repeats validation, verifies the collected checksums, then uses the
environment-scoped OIDC identity with `vsce publish --oidc`. Only that final job has
`id-token: write` permission.

Check the workflow result and the [Marketplace listing](https://marketplace.visualstudio.com/items?itemName=kidoz.dotprolog).
Marketplace processing can continue after upload. Confirm all four packages appear, install the
stable release from Marketplace on each supported OS, and verify the listing is not marked
**Preview** or offered only through the pre-release channel before announcing availability.

Keep numeric `major.minor.patch` versions increasing. The workflow does not tag the repository,
publish NuGet packages or change the core release flow.

## Maintain the visual assets

`src/DotProlog.VSCode/images/` contains editable `icon.svg` and `banner.svg` masters. The icon
continues the repository's navy, purple and cyan identity. Export PNGs with librsvg:

```sh
cd src/DotProlog.VSCode/images
rsvg-convert -w 256 -h 256 icon.svg -o icon.png
rsvg-convert -w 512 -h 512 icon.svg -o icon-512.png
rsvg-convert banner.svg -o banner.png
```

The VSIX includes PNG branding and JPEG screenshots; SVG masters stay in the source tree.
Refresh `editor.jpg` and `diagnostics.jpg` from the actual extension UI when features change.
Keep sample workspaces free of account details and private code.
