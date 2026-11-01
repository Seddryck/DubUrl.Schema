# Release and documentation delivery

The libraries and reusable NUnit contracts are portable multi-target NuGet packages (`net8.0`, `net9.0`, `net10.0`). They share the version in `version.json`; the DubUrl dependency has its own pinned version in `Directory.Build.props`. No executable RID, libc, or architecture distribution matrix is needed. Native SQLite/DuckDB tests retain Windows runners; PostgreSQL, SQL Server, Oracle, SingleStore and MySQL use Linux containers. No new macOS or ARM support commitment is introduced.

CI validates every TFM, keeps coverage enforcement separate, validates NuGet contents and runs package consumer tests before publishing. `schema-docs.zip` is a reproducible Jekyll source bundle, including page front matter and stable specification paths. No theme or hosting configuration is included. The single website remains in Seddryck/DubUrl.

```powershell
python build/build_docs.py
```

On the default branch, run the CI workflow with `publish=true` to release the version in `version.json`. Update both the version file and the default build version for the next release. Configure a NuGet trusted-publishing policy for Seddryck/DubUrl.Schema, workflow `ci.yml`, environment `release`, using repository variable `NUGET_USER`. Publication is gated by all database jobs and package validation. GitHub release assets are uploaded only after NuGet publication succeeds. Publication can be retried for the same commit/version; it must not replace artifacts from another commit.

For the first extraction, merge this repository and publish 1.2.0 before merging DubUrl's consuming PR. DubUrl initially uses the already-published 0.55.1 Schema packages from the identical source baseline; update that pin to 1.2.0 and regenerate CLI/worker lockfiles after publication. In that PR pin the exact release ZIP SHA-256 and source commit (use the manifest of the published asset, especially after a squash merge) in `docs/schema-docs.lock.json`; build with `python build/import_schema_docs.py`. Earlier document URLs and `$id` values remain unchanged. Use `--bundle <local zip>` for offline verification of the same pin. Build the website from `artifacts/docs-site`, never from the incomplete source-only `docs` directory.

Schema PRs upload the docs ZIP as a CI artifact. Download it locally and use a temporary lock matching its version, commit and checksum with the main repository importer (`--pin <preview lock> --bundle <artifact>`). Production remains pinned to a reviewed release. Update the pin through a normal DubUrl PR after each Schema documentation release.

For cross-repository compatibility, dispatch CI with `duburl_version` set to a candidate NuGet version. Those runs never publish. This exercises the same build and database tests against core/provider packages without adding a source checkout dependency.
