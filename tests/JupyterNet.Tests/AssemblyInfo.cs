// xUnit parallelizes different test classes by default. FSharpKernel redirects the process-wide
// Console.Out/Error for the duration of each EvalInteractionNonThrowing call (see FSharpKernel.cs
// — it's how printfn output gets captured); two FSharpKernel instances executing on different
// threads at the same time race on that shared global and can silently lose/misattribute output
// (found exactly this way: PrintfnIsCapturedWithoutFsiEcho flaked to an empty output collection
// under default parallelization, passed reliably standalone). This mirrors a real constraint of
// the kernels themselves, not just a test artifact — JupyterNet.Host's own request loop is
// strictly sequential for the same reason. Tests here run sequentially to match.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
