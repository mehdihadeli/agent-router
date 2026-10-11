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

Current preview configuration:

```json
{
  "version": "10.0.0-preview.{height}",
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

| Source ref | Effective version | Environment | Publication |
| --- | --- | --- | --- |
| `main`, preview height `0` | `10.0.0-preview.0` | none | Validation only |
| `main`, preview height `1+` | `10.0.0-preview.N.YYDDD.RUN_NUMBER` | dev | NuGet package |
| `v10.0.0-rc.1` | `10.0.0-rc.1.YYDDD.RUN_NUMBER` | staging | NuGet package |
| `v10.0.0` | `10.0.0` | production | NuGet package |

`YYDDD` is the UTC two-digit year and day of year. `RUN_NUMBER` is the
monotonically increasing GitHub Actions workflow run number. The date and run
number distinguish separately built preview or RC packages; they do not replace
the NBGV preview or RC ordinal.

The workflow publishes the `AgentRouter` CLI package to NuGet.org. It
does not currently publish self-contained archives, container images, or deploy
to an environment. The environment names describe release intent and CI
permissions.

## Preview releases

Do not edit `preview.1` to create `preview.2`. NBGV derives the ordinal from
Git commit height. Ordinary feature, fix, test, and documentation pull
requests should not change `version.json`.

The initial preview-train commit is validation-only when it calculates
`preview.0`. The first ordinary merge after it publishes `preview.1`; later
accepted changes publish the next preview number.

Start a new preview train with a reviewed version-intent change:

```bash
./release-version.sh prepare-train 10.1.0
git add version.json
git commit -m "chore: start 10.1.0 preview train"
git push
```

The helper changes the version to `10.1.0-preview.{height}`. It does not
commit, push, or publish.

## RC and stable releases

RC and stable version changes are prepared on short-lived branches and merged
through the protected `main` branch. Tag the merged commit only after CI
validation succeeds.

Prepare the first RC train:

```bash
./release-version.sh prepare-rc 10.0.0
git add version.json
git commit -m "chore: prepare 10.0.0 RC train"
git push -u origin chore/prepare-10.0.0-rc
```

After the pull request is merged and local `main` is updated, create and push
the tag:

```bash
./release-version.sh tag
git push origin v10.0.0-rc.1
```

Later RC fixes do not edit `version.json`; another merge advances the
`10.0.0-rc.{height}` template to the next RC ordinal.

Prepare stable in the same way:

```bash
./release-version.sh prepare-stable 10.0.0
git add version.json
git commit -m "chore: prepare 10.0.0"
git push -u origin chore/prepare-10.0.0
```

After that change is merged and validated:

```bash
./release-version.sh tag
git push origin v10.0.0
```

The stable tag must point to the merged stable-preparation commit, and CI
publishes clean `10.0.0` package metadata. The release helper never commits or
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
2. `calculate-version` runs [`calculate-version.sh`](../../calculate-version.sh),
   validates release tags, and selects the effective version and environment.
3. `publish` packs `AgentRouter`, pushes it to NuGet.org, and updates or
   publishes the matching Release Drafter release.

For a syntactically valid release tag, CI removes the `v` prefix and compares
the tag with NBGV's calculated version after removing only `.g<commit>`. A
mismatch fails the workflow, so a tag such as `v10.0.0-rc.3` cannot publish a
commit that calculates as `10.0.0-rc.2`.

Release Drafter runs with `if: always()` so release notes remain up to date when
an earlier package publication step fails. The job still fails and the package
must be fixed and rerun.

## Quick checks

Before pushing a release tag, run:

```bash
bash -n calculate-version.sh
bash -n release-version.sh
dotnet test --solution agent-router.slnx --configuration Release
dotnet pack src/AgentRouter.Cli/AgentRouter.Cli.csproj --configuration Release --output artifacts
```

Then inspect `version.json`, confirm the tag was created from the merged `main`
commit, and push only that tag.
