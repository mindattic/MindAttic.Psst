Deploy the MindAttic.Psst landing page (`mindattic.com/mindatticpsst.htm`) via **MindAttic.Deploy** (sibling repo at `D:\Projects\MindAttic\MindAttic.Deploy`).

Renders this repo's `README.md` through the catalog template and FTPS-uploads the single-file result.

Run this command and report the result:

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "cd D:\Projects\MindAttic\MindAttic.Deploy; npm run deploy -- --only mindatticpsst"
```

Notes:
- Catalog entry: `MindAttic.Deploy/projects.json` -> `projects[]` slug `mindatticpsst` (theme: Cyberspace).
- Credentials: `MindAttic.Deploy/secrets/ftp.json` (gitignored).
- MindAttic.Psst itself is a CLI tool (no app deploy target) -- this command only ships the landing page.
