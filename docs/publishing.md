# Maintaining the documentation

[Analyzer catalog](../README.md#analyzer-catalog)

The GitHub Pages site uses DocFX's modern template with a small CSS/JavaScript overlay. There is no separate frontend build, analytics service, or external font dependency.

## Build and preview

Install the .NET 11 SDK to match the documentation workflow. From the repository root:

```powershell
dotnet tool restore
dotnet docfx build docfx.json --warningsAsErrors
dotnet docfx serve _site --port 8080
```

Open `http://localhost:8080`. After editing a file, rebuild the site and refresh the browser. Stop the server with Ctrl+C.

The tool version is pinned in `dotnet-tools.json`. Generated HTML goes into the ignored `_site` directory; do not commit it.

## Content and navigation

- `README.md` owns the analyzer catalog and remains available as `README.html` on the site.
- `docs\index.md` is the site's landing page, published as `index.html`.
- Other Markdown files under `docs\` are published at matching `docs/*.html` paths.
- `toc.yml` defines the top navigation; `docs\toc.yml` defines the documentation sidebar.

Keep rule details in `docs\rules\WRANGLER####.md`. Update the README catalog and sidebar when adding a rule; do not duplicate the full reference content on the landing page.

Use relative Markdown links between included pages. DocFX rewrites them to their generated HTML destinations, including links from the relocated landing page. Use GitHub URLs for source files that are not published as documentation.

Preserve README rule headings: existing analyzer help links still target those anchors. Move diagnostic help links to the site only as a separate, deliberate change after publishing it.

## Customize the appearance

The `template` list in `docfx.json` layers `docs\template` over DocFX's built-in `default` and `modern` templates.

Edit `docs\template\public\main.css` for colors, typography, spacing, and responsive layouts. The light/dark theme variables are defined at the top. `main.js` uses DocFX's supported options for automatic theme selection and the GitHub header link.

The landing page's HTML is kept in `docs\index.md`. Standard rule and guide pages still use the built-in layout, navigation, search, syntax highlighting, and copy buttons. No upstream template files are forked.

See the [DocFX template documentation](https://dotnet.github.io/docfx/docs/template.html) for supported customization options.

## Publish to GitHub Pages

The repository's **Settings > Pages > Source** must be **GitHub Actions**.

The [documentation workflow](https://github.com/BenjaminMichaelis/DotNetWrangler/blob/main/.github/workflows/docs.yml) builds changes to documentation, navigation, configuration, or the tool manifest:

- Pull requests build with warnings treated as errors, without deployment credentials.
- Pushes to `main` build and publish through the `github-pages` environment.
- A manual workflow run can rebuild the site; only runs on `main` deploy.

The workflow uploads `_site` as a Pages artifact. There is no generated-content commit or `gh-pages` branch.

The site URL is `https://benjaminmichaelis.github.io/DotNetWrangler/`. Internal assets and navigation use relative URLs so the repository subpath works. If the repository or domain changes, update the sitemap base URL in `docfx.json` and the documentation link in the README.
