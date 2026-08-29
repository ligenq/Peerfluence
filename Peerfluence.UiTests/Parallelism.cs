using Xunit.Sdk;
using Xunit.v3;

// One test at a time. These drive the desktop: they launch windows that take focus, and two running
// at once means one test's keystrokes land in another test's application. The first run with several
// test classes had four of them fail within the same seventieth of a second, all during startup.
[assembly: Parallelization(Mode = ParallelMode.None)]
