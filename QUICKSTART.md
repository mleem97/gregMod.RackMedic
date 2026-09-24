# Quickstart — gregMod.RackMedic

> Copy `gregMod.RackMedic.dll` to `Data Center/Mods/`. Default shop/workbench keys are F8/F7.

Repo: [https://github.com/mleem97/gregMod.RackMedic](https://github.com/mleem97/gregMod.RackMedic) · Version: `0.1.0` · License: Apache-2.0.

## 1. Clone

```bash
git clone https://github.com/mleem97/gregMod.RackMedic.git
cd gregMod.RackMedic
```

## 2. Build / Run

Depending on the tech stack, choose **one** path:

```bash
# .NET
dotnet build -c Release
dotnet run --project src/

# Node / pnpm
pnpm install
pnpm build
pnpm start

# Python
python -m venv .venv && source .venv/bin/activate
pip install -r requirements.txt
python -m <modul>
```

## 3. Test

```bash
dotnet test            # .NET
pnpm test              # Node
pytest                 # Python
```

Details are in [README.md](README.md) and [docs/INDEX.md](docs/INDEX.md).
If you run into problems: open an issue ([Issues](https://github.com/mleem97/gregMod.RackMedic/issues)) or read [CONTRIBUTING.md](CONTRIBUTING.md).
