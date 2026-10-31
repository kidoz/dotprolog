# Check package vulnerabilities and licenses

`propack.policy.json` is the base for both checks. It sits beside the lock, and schema 1 manifests
stay unchanged. The lock still proves the bytes. The policy decides which of those bytes are
acceptable.

Put the file in the project root. When it is absent, restore and compilation behave as before.
When it is present, `restore`, `update`, `check`, `tree`, `run`, and compilation all apply it
after the tree hash matches. A failure leaves the existing lock in place and does not write a new one.

```json
{
  "schema": 1,
  "advisoryFiles": ["advisories.json"],
  "ignore": ["PROPACK-2026-0001"],
  "licenses": ["MIT", "BSD-2-Clause", "Apache-2.0 WITH LLVM-exception"],
  "advisories": [
    {
      "id": "PROPACK-2026-0002",
      "package": "demo/lib",
      "version": "1.2.0",
      "summary": "Known bad release."
    }
  ]
}
```

The [format reference](../reference/propack-format.md) is the field contract. Unknown fields are rejected.

## Vulnerabilities

An advisory matches a locked package by name plus every version and tree hash you supply. One of
those two is required. `advisoryFiles` points at extra `{ "schema": 1, "advisories": [] }` files
inside the project, so a feed can be vendored without mixing it into the allow list. `ignore`
accepts an id only when that id is actually loaded. There is no network feed: the committed files
are the whole database. The root application is included.

## Licenses

Omit `licenses` and this filter stays off. Set it, and every package, including the root, must
declare `license` in `UPSTREAM.json`. `LICENSE.txt` is not parsed. The value is an SPDX expression:
`AND` requires both sides, `OR` requires one, and `Apache-2.0 WITH LLVM-exception` is a single
allow-list entry.

## Run the check

`propack check` runs the filters against the current lock. With no policy file it reports that the
filters were not applied.

```sh
propack check --project samples/PortablePackages/app
dotnet prolog package check --project samples/PortablePackages/app
```

Restore, update, and compilation run the same filters whenever the policy file is present.
