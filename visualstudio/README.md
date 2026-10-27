# OpenTips for Visual Studio (experimental)

This is a partial native Visual Studio Community 2026 integration for OpenTips. It starts the existing Python service and watches saved files under the solution directory. It lists suggestions, opens their locations, and dismisses them. It does not yet support Copilot, inline highlights, apply, explain, or unsaved buffers. The solution directory should be the Git repository root.

The service sends changed code to Anthropic for review. Enter an Anthropic API key in the tool window each session; it is passed to the Python process in its environment and is not saved by this extension. Open View > Other Windows > OpenTips, enter the key, and select Start review.

Install Python 3.11 or newer and run `python -m venv .venv` and `.venv\Scripts\python -m pip install opentips` in the checkout at `%USERPROFILE%\source\opentips-visualstudio`. Alternatively set the `OPENTIPS_PYTHON` environment variable to the Python executable in an environment containing `opentips`, then restart Visual Studio.

Build the VSIX with Visual Studio MSBuild: `MSBuild.exe visualstudio\OpenTips.VisualStudio.csproj -restore -p:Configuration=Release`. Run `.venv\Scripts\python visualstudio\smoke_test.py` to verify the Python service RPC endpoint without a model key.
