# Contributing to Lucent

Thanks for wanting to help. Lucent is a small, carefully kept codebase, so a few rules keep it that way.

## Open an issue first

Before you start on a new feature, a redesign, or a refactor, open an [issue](../../issues) so we can talk it through. Small bug fixes and typo fixes can go straight to a pull request.

Please don't send AI-generated refactors or feature overhauls nobody asked for. Pull requests like that may be closed without review. Using tools to help you write code is fine, as long as you understand and have tested every line you send.

## Where help is most welcome

- **New visuals.** Each one is a single HLSL shader in [`content/Presets`](content/Presets). The README shows a small example, and [`content/Shaders/Common.hlsli`](content/Shaders/Common.hlsli) lists the inputs and helpers.
- **Audio and DSP.** Analysis, the equalizer, and the editor's effects live in [`src/Lucent.Core`](src/Lucent.Core).
- **Bug reports** with steps to reproduce. Lucent's **🐞 Report a bug** button fills in the details for you.

## Ground rules

- **Windows and Mac stay at feature parity.** A change to one app goes into the other in the same pull request. The few exceptions are in [`docs/PARITY.md`](docs/PARITY.md).
- **Keep it focused.** One change per pull request, matching the style of the code around it.
- **Make sure it builds.** CI builds both apps and checks the shaders on every pull request, and it has to pass before anything merges.
- **Credit what isn't yours.** Presets, skins, and other art need their author's permission or an open licence, credited in the folder's `CREDITS.md`.

Where the code lives is in [`docs/LAYOUT.md`](docs/LAYOUT.md), and what each feature does is in [`docs/specs`](docs/specs).

By contributing, you agree your work is released under the [MIT License](LICENSE).
