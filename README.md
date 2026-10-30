# Usine agentique

Une application web décrite en langage courant est conçue, codée et testée par des agents d'IA.

## Prérequis

Windows, avec PowerShell 7 et GNU make.

## Installer

```bash
make install
```

Installe ce qui manque (SDK .NET 10, Node.js, Docker Desktop), puis reconstruit tout de zéro : l'usine, l'écran et l'image Docker du bac à sable.

## Lancer

```bash
make run
```

Ouvre l'adresse affichée, puis, dans **Paramètres**, choisis une IA et colle ta clé d'API.
