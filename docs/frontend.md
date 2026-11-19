# Frontend

Websiter places no restrictions on frontend technology: HTML5, CSS3, modern JavaScript, ES modules, images, fonts, videos, and any framework (React, Vue, Svelte, vanilla) all work — they're just static assets.

## Directory layout

```text
wwwroot/           # served at /
├── css/main.css
├── js/app.js
├── img/logo.svg
└── fonts/...
Styles/  Scripts/  # optional source dirs processed by the asset pipeline
```

## Asset pipeline

`websiter build` (and `dev` in watch mode) processes frontend assets:

- Content hashing / fingerprinting for cache busting (`app.a1b2c3.js`)
- A manifest (`wwwroot/assets.json` or `.websiter/manifest.json`) mapping logical → hashed names
- Correct `Content-Type` per extension, `nosniff`
- Long-lived `Cache-Control` for fingerprinted assets in production

Reference hashed assets via the manifest in your HTML or resolve at runtime with the provided helper.

## Live reload (dev)

`websiter dev` watches `wwwroot/`, `Pages/`, `Components/`, `Styles/`, `Scripts/`:

- Frontend-only changes → browser reload (injected reload script in dev pages)
- C#/worker changes → rebuild + restart of the affected process only

## Serving notes

- HTML files under `wwwroot/` also get `<component>` expansion.
- `Pages/about.html` is served at `/about`; `Pages/index.html` at `/`.
- Explicit routes always win over file/page fallbacks.
