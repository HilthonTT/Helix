# Description

<!-- What does this PR change, and why? Link the issue it closes, e.g. "Closes #12". -->

## Type of change

- [ ] Bug fix (non-breaking change that fixes an issue)
- [ ] New feature (non-breaking change that adds functionality)
- [ ] Breaking change (existing behaviour changes)
- [ ] Refactor / chore / docs (no functional change)

## Checklist

- [ ] `dotnet build Helix.slnx` succeeds
- [ ] All three test suites pass (Application, Infrastructure, Architecture)
- [ ] New use cases follow the handler pattern and are registered in `Helix.Application/DependencyInjection.cs`
- [ ] No new user-facing strings are hard-coded — they live in `AppResources.resx` and are used via `TranslateExtension`
- [ ] Layer dependencies are unchanged or still satisfy the NetArchTest rules
- [ ] No secrets, credentials or personal data added to the repository

## Screenshots

<!-- For UI changes, add before/after screenshots. Delete this section otherwise. -->
