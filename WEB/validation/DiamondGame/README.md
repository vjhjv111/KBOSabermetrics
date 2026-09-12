# Diamond API regression

Run after `npm ci` in `WEB/diamond-game`. The fixture generator bundles the unchanged TypeScript engine and fixes its random stream, then the .NET test compares the C# engine against those results.

From `WEB`, replace the output paths with an absolute temporary directory:

```powershell
node validation/DiamondGame/generate-fixtures.mjs diamond-game/lib C:/temp/diamond-fixtures.json
dotnet run --project validation/DiamondGame/DiamondGame.Validation.csproj -- C:/absolute/project/WEB/diamond-game/lib C:/temp/diamond-fixtures.json C:/temp/diamond-validation
```

The runner uses only a new game database under the supplied output directory. It does not open the baseball record database. Its HTTP test binds a temporary loopback port and uses ephemeral data-protection keys.

Checks include original-engine numeric parity for every supplied pitcher/arsenal, both AI roles, PvP membership and concurrent joining, repeated commands, six-plate outcomes, persistent matches, expiration, creation limits, CSRF, cookies, cross-origin rejection, and bounded request bodies.
