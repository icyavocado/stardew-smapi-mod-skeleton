# Stardew SMAPI Mod Skeleton — Development README

This repository is a minimal Stardew Valley SMAPI mod skeleton intended for quick mod development and as a template for new mods.

Prerequisites
- Install Stardew Valley + SMAPI to `install_directory`

## Quick build (run command inside docker)

1. Run docker container

```bash
docker compose build
docker compose up -d
docker compose exec stardew_valley_mod bash
```

2. Build the example `HelloWorld` mod in Release mode:

```bash
dotnet build src/HelloWorld/HelloWorld.csproj
```

2. After a successful build the Mods should be ready to be loaded into the `mods` directory

3. To restart Stardew Valley and the game

```bash
cd /home/app
supervisorctl restart stardewvalley
```
