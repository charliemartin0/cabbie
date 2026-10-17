## Summary

<!-- One intent. What changes, and why. -->

## Stack

<!-- Base branch and position, e.g. "2 of 4, on #71". Say "off main" if not stacked. -->

## Behaviour covered

<!-- The prove-with behaviours this PR pins, as test names or one-line sentences. -->

## Checks

- [ ] `dotnet build Cabbie.slnx` (0 warnings; analyzers are errors)
- [ ] `dotnet test Cabbie.slnx`
- [ ] `dotnet test tests/Cabbie.Tests.Postgresql` (Docker), or why it was skipped
- [ ] `dotnet format` clean on changed `.cs` files
- [ ] No new analyzer baseline lines, `#pragma` or `NoWarn`
- [ ] `src/Cabbie` still references no adapter project

## Review notes

<!-- Anything a reviewer should look at first: risks, follow-ups, decisions. -->
