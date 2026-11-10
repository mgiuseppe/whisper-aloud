# Claude Code

Instructions for Claude Code in this repository. The project rules are those for every contributor:

@CONTRIBUTING.md

## Working here

- Commit and push directly to `main`, with commit messages in English. Commit only the files you changed.
- Keep the build free of warnings and run `dotnet test` before committing code.
- When you change something users can see, update the docs in the same commit: README for usage, `docs/` for details, screenshots when the interface changes.

## Releases

Do not ask about a release at every commit. After pushing, check whether anything that reaches users changed since the last release:

```bash
git describe --tags --abbrev=0
```

```bash
git diff --stat <last tag>..HEAD -- src .github
```

If that diff is empty, say nothing about releases. Otherwise, at the end of the block of work:

1. Propose a release: the version number, chosen from the commits since the last tag following **Version number** in CONTRIBUTING, and a draft of the release notes following **Release notes**.
2. Wait for an explicit yes from the user on both. A tag publishes a release that anyone can download, so never tag on your own initiative.
3. Check the exe as described in **Check the exe first**. If it does not start, Whisper does not load or the log shows errors, stop and report instead of tagging.
4. Create the annotated tag with the approved notes and push it, as in **Tag and push**. Then follow the workflow run (`gh run watch`), check that the release has the exe and the notes, and give the user the link.
