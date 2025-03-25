# Microsoft.Build.Tasks.CodeAnalysis

This MSBuild tasks contains the core tasks and targets for compiling C# and VB projects.  

## Debugging

In VSCode, use one of `Microsoft.Build.Tasks.CodeAnalysis.dll` launch targets.
In VS, replicate its behavior:
1. Execute `./scripts/build-tasks.ps1`.
1. Debug an external program "MSBuild.exe -restore" or "dotnet.exe build" with "-p:RoslynTargetsPath=" pointing to the output of `build-tasks.ps1`.
