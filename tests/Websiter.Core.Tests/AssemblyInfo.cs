using Xunit;

// Config/environment tests mutate process-wide static state (Config singleton,
// environment variables), so they must not run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
