# Components

Reusable HTML fragments in `Components/` included with `<component>` tags.

## Basics

`Components/Navbar.html`:

```html
<nav class="navbar"><a href="/">Home</a><a href="/about">About</a></nav>
```

`Pages/index.html`:

```html
<html><body>
<component name="Navbar" />
<h1>Welcome</h1>
</body></html>
```

Renders to:

```html
<nav class="navbar">...</nav>
<h1>Welcome</h1>
```

## Nesting

Components can include other components, arbitrarily deep (default max depth 32):

```html
<!-- Components/Card.html -->
<div class="card"><component name="layout/Header" /></div>
```

Subdirectory components use `/` in the name: `<component name="layout/Header" />` resolves `Components/layout/Header.html`.

## Attributes

`<component name="..." />` — `name` is required (single, double, or unquoted). Other attributes are currently ignored; component parameters/slots are planned.

## Safety rules

- Names are restricted to letters, digits, `-`, `_` — no path traversal.
- `..` is rejected in subdirectory references.
- Missing components throw a clear `ComponentException` naming the component and directory.
- Circular includes (A→B→A, self-include) are detected and rejected.
- **No server-side code execution**: component content is inserted verbatim. Components are trusted developer markup — escape user data yourself.

## Caching & watching

Rendered output is cached per component and invalidated by file timestamp. In dev, `EnableWatching()` (enabled by `websiter dev`) watches `Components/` and clears caches on change.

## API

```csharp
var engine = new ComponentEngine("Components");
var html = engine.Render(source);          // expand all <component> tags
engine.RenderComponent("Navbar");         // render one by name
engine.Discover();                          // list available components
engine.Invalidate();                        // clear caches
```
