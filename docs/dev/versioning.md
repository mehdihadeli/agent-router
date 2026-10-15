# Release versioning

Agent Router uses Nerdbank.GitVersioning (NBGV) with GitHub Flow.
`main` is the long-lived branch. Changes arrive through pull requests, and
release tags are created only from approved commits on `main`.

The release contract is:

- Ordinary merges advance a commit-height preview version.
- RC and stable releases start with a reviewed change to `version.json`.
- CI validates release tags against the version calculated by NBGV.
- Preview and RC packages include a UTC date and GitHub Actions run number.
- Stable packages use the clean `MAJOR.MINOR.PATCH` version.

## Sources of truth

[`version.json`](../../version.json) contains version intent. NBGV calculates
SemVer, assembly metadata, and commit height from that file and Git history.
The NBGV CLI version used by the workflow is pinned to `3.10.94`.

Current preview configuration (version-related excerpt):

```json
{
  "version": "1.0.0-preview.{height}",
  "publicReleaseRefSpec": [
    "^refs/heads/main$",
    "^refs/tags/v\\d+\\.\\d+\\.\\d+(?:-rc\\.\\d+)?$"
  ]
}
```

Use the same calculation locally and in CI:

```bash
dotnet tool install --tool-path ./.tools/nbgv nbgv --version 3.10.94
./.tools/nbgv/nbgv get-version -v SemVer2
```

An untagged commit can include `.g<commit>` in the calculated version. That
suffix identifies the source commit and is not part of a public release tag.

## Version shapes

The workflow uses these effective package versions:

| Source ref | Effective package version | Release intent | Publication |
| --- | --- | --- | --- |
| `main`, preview height `0` | `1.0.0-preview.0` | none | Validation only |
| `main`, preview height `1+` | `1.0.0-preview.N.YYDDD.RUN_NUMBER` | dev | NuGet package and release assets |
| `v1.0.0-rc.1` | `1.0.0-rc.1.YYDDD.RUN_NUMBER` | staging | NuGet package and release assets |
| `v1.0.0` | `1.0.0` | production | NuGet package and release assets |

`YYDDD` is the UTC two-digit year and day of year. `RUN_NUMBER` is the
monotonically increasing GitHub Actions workflow run number. The date and run
number distinguish separately built preview or RC packages; they do not replace
the NBGV preview or RC ordinal.

For example, preview ordinal `7`, built on October 14, 2026 (UTC day `287`),
with workflow run number `42` produces `1.0.0-preview.7.26287.42`.
The matching RC tag `v1.0.0-rc.1` produces package version
`1.0.0-rc.1.26287.42`; stable tag `v1.0.0` produces `1.0.0`.
Any calculated `.g<commit>` suffix is excluded from published preview and RC
package versions. At preview height `0`, the script leaves the calculated NBGV
version unchanged, including any such suffix, and skips publication.

The workflow publishes the `AgentRouter` CLI package to NuGet.org and uploads
the package and self-contained, single-file executable archives to the matching
GitHub release:

- `agent-router-linux-x64.tar.gz`, containing `ar`.
- `agent-router-osx-x64.tar.gz`, containing `ar`.
- `agent-router-osx-arm64.tar.gz`, containing `ar`.
- `agent-router-win-x64.zip`, containing `ar.exe`.

It does not publish container images or deploy the application. The calculated
`dev`, `staging`, and `production` labels describe release intent; the publish
job uses the GitHub environment `nuget` for all published versions.

The pack step overrides `PackageVersion` with the effective version above.
The script also outputs `assembly_version`, `informational_version`
(`EFFECTIVE_VERSION+COMMIT`), `docker_tag`, and `commit`, but the workflow does
not pass those metadata outputs to pack or executable publish. Executable
metadata therefore follows the project's NBGV build configuration, not an
explicit override with the date/run package version.

## Preview releases

Do not edit `preview.1` to create `preview.2`. NBGV derives the ordinal from
Git commit height. Ordinary feature, fix, test, and documentation pull
requests should not change `version.json`.

The initial preview-train commit is validation-only when it calculates
`preview.0`. The first ordinary merge after it publishes `preview.1`; later
accepted changes publish the next preview number.

Start a new preview train with a reviewed version-intent change:

```bash
./scripts/release-version.sh prepare-train 1.1.0
git add version.json
git commit -m "chore: start 1.1.0 preview train"
git push
```

The helper changes the version to `1.1.0-preview.{height}`. It does not
commit, push, or publish. The release helper requires `dotnet nbgv`; install
the pinned NBGV CLI as a local tool if it is not already available.

## RC and stable releases

RC and stable version changes are prepared on short-lived branches and merged
through the protected `main` branch. Tag the merged commit only after CI
validation succeeds.

Prepare the first RC train:

```bash
./scripts/release-version.sh prepare-rc 1.0.0
git add version.json
git commit -m "chore: prepare 1.0.0 RC train"
git push -u origin chore/prepare-1.0.0-rc
```

After the pull request is merged and local `main` is updated, create and push
the tag:

```bash
./scripts/release-version.sh tag
git push origin v1.0.0-rc.1
```

Later RC fixes do not edit `version.json`; another merge advances the
`1.0.0-rc.{height}` template to the next RC ordinal. Use the tag reported by
NBGV rather than assuming an ordinal; CI validates it before adding the
date/run suffix to the package version.

Prepare stable in the same way:

```bash
./scripts/release-version.sh prepare-stable 1.0.0
git add version.json
git commit -m "chore: prepare 1.0.0"
git push -u origin chore/prepare-1.0.0
```

After that change is merged and validated:

```bash
./scripts/release-version.sh tag
git push origin v1.0.0
```

The stable tag must point to the merged stable-preparation commit, and CI
publishes clean `1.0.0` package versions. The release helper never commits or
pushes on your behalf.

## CI behavior

[`build-and-publish.yml`](../../.github/workflows/build-and-publish.yml) runs
build and tests for pull requests and pushes. Version calculation and package
publication run only for `main` and `v*` tags.

Every version-calculating job checks out full history:

```yaml
- uses: actions/checkout@v5
  with:
    fetch-depth: 0
```

The workflow uses these stages:

1. `build-test` restores, builds, and tests the solution.
2. `calculate-version` runs [`calculate-version.sh`](https://github.com/mehdihadeli/agent-router/blob/main/scripts/calculate-version.sh),
  validates release tags, and selects the effective package version and release
  intent label.
3. `publish` packs `AgentRouter`, pushes it to NuGet.org, builds executable
  archives, updates or publishes the matching Release Drafter release, and
  uploads the package and archives as release assets.

Preview releases remain drafts with a tag/name based on the effective package
version. RC and stable releases are published under the triggering source tag;
the RC release name includes the date/run suffix even though its tag does not.

For a syntactically valid release tag, CI removes the `v` prefix and compares
the tag with NBGV's calculated version after removing only `.g<commit>`. A
mismatch fails the workflow, so a tag such as `v1.0.0-rc.3` cannot publish a
commit that calculates as `1.0.0-rc.2`. Other `v*` tag formats are rejected by
the calculation script.

Release Drafter runs with `if: always()` so release notes remain up to date when
an earlier package publication step fails. The job still fails and the package
must be fixed and rerun.

## Quick checks

Before pushing a release tag, run:

```bash
bash -n scripts/calculate-version.sh
bash -n scripts/release-version.sh
dotnet test --solution agent-router.slnx --configuration Release
dotnet pack src/AgentRouter.Cli/AgentRouter.Cli.csproj --configuration Release --output artifacts
```

Then inspect `version.json`, confirm the tag was created from the merged `main`
commit, and push only that tag.
