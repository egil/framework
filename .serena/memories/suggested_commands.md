# Suggested commands
- Show repo status: `git status --short`
- Show current branch: `git branch --show-current`
- Run StateMigration tests (Release): `dotnet test Egil.Orleans.StateMigration/Egil.Orleans.StateMigration.sln -c Release`
- Run package upgrade check for library project: `dotnet outdated -u Egil.Orleans.StateMigration/src/Egil.Orleans.StateMigration/Egil.Orleans.StateMigration.csproj`
- Run package upgrade check for test project: `dotnet outdated -u Egil.Orleans.StateMigration/test/Egil.Orleans.StateMigration.Tests/Egil.Orleans.StateMigration.Tests.csproj`
- Build library (Release): `dotnet build Egil.Orleans.StateMigration/src/Egil.Orleans.StateMigration/Egil.Orleans.StateMigration.csproj -c Release -f net10.0`
- Common Linux shell utilities in this environment: `ls`, `find`, `rg`, `sed`, `cat`, `git`.