SHELL := pwsh.exe
.SHELLFLAGS := -NoProfile -NoLogo -Command

CE_MAKEFILE := $(abspath $(lastword $(MAKEFILE_LIST)))
ICI := $(patsubst %/,%,$(dir $(CE_MAKEFILE)))
CLI := $(ICI)/src/Usine.Cli
USINE := $(CLI)/bin/Release/net10.0/usine.exe

export USINE_HOME USINE_CONFIG DEMANDE DEPOT TITRE ID ETAPE MOTIF REPONSES MODELE FICHIER NOUVEAUX_TESTS PROJET PORT

PREPARER = $$ErrorActionPreference = 'Stop'; $$verrouille = "usine.exe est verrouill`u{e9} par un worker encore en marche : lancement de la version d`u{e9}j`u{e0} construite, sans les derni`u{e8}res modifications."; $$dll = '$(CLI)/bin/Release/net10.0/usine.dll'; $$libre = $$true; if (Test-Path -LiteralPath $$dll) { try { [IO.File]::Open($$dll, 'Open', 'ReadWrite', 'None').Dispose() } catch { $$libre = $$false } }; if (-not $$libre) { Write-Host $$verrouille } else { $$sortie = dotnet build '$(CLI)' -c Release -v q -p:CopyRetryCount=0 2>&1; if ($$LASTEXITCODE -ne 0) { if ((Test-Path -LiteralPath '$(USINE)') -and ($$sortie -match 'MSB3021|MSB3027')) { Write-Host $$verrouille } else { $$sortie | Write-Host; exit 1 } } };
CONSTRUIRE_ECRAN = $$f = '$(ICI)/front'; if (Test-Path -LiteralPath "$$f/package.json") { if (-not (Get-Command node -ErrorAction SilentlyContinue)) { Write-Host "Node.js est n`u{e9}cessaire pour construire l'`u{e9}cran de l'usine : installe-le (version 24 ou plus), puis relance make interface."; exit 1 }; Push-Location -LiteralPath $$f; $$verrou = "$$f/node_modules/.package-lock.json"; if (-not (Test-Path -LiteralPath $$verrou) -or ((Test-Path -LiteralPath "$$f/package-lock.json") -and (Get-Item -LiteralPath "$$f/package-lock.json").LastWriteTimeUtc -gt (Get-Item -LiteralPath $$verrou).LastWriteTimeUtc)) { $$sortie = npm ci 2>&1; if ($$LASTEXITCODE -ne 0) { $$sortie | Write-Host; exit 1 } }; $$index = "$$f/dist/front/browser/index.html"; $$sources = @(Get-ChildItem -LiteralPath "$$f/src", "$$f/openapi" -Recurse -File -ErrorAction SilentlyContinue | Where-Object FullName -notlike '*\src\app\api\schema.d.ts') + @(Get-ChildItem -LiteralPath $$f -File | Where-Object { $$_.Name -in @('package.json', 'package-lock.json', 'angular.json') -or $$_.Name -like 'tsconfig*.json' }); if (-not (Test-Path -LiteralPath $$index) -or ($$sources | Where-Object LastWriteTimeUtc -gt (Get-Item -LiteralPath $$index).LastWriteTimeUtc)) { $$sortie = npm run build 2>&1; if ($$LASTEXITCODE -ne 0) { $$sortie | Write-Host; exit 1 } }; Pop-Location };
INSTALLER_LOGICIELS = $$installes = @(); if (-not (Get-Command winget -ErrorAction SilentlyContinue)) { Write-Host "winget est introuvable : installe le SDK .NET 10, Node.js 24 ou plus et Docker Desktop, puis relance make install."; exit 1 }; if (-not ((Get-Command dotnet -ErrorAction SilentlyContinue) -and ((dotnet --list-sdks) -match '^10\.'))) { winget install --id Microsoft.DotNet.SDK.10 -e --silent --accept-package-agreements --accept-source-agreements; if ($$LASTEXITCODE -ne 0) { Write-Host "L'installation de SDK .NET 10 a `u{e9}chou`u{e9}."; exit 1 }; $$installes += 'SDK .NET 10' }; if (-not (Get-Command node -ErrorAction SilentlyContinue) -or [int]((node --version).TrimStart('v').Split('.')[0]) -lt 24) { winget install --id OpenJS.NodeJS.LTS -e --silent --accept-package-agreements --accept-source-agreements; if ($$LASTEXITCODE -ne 0) { Write-Host "L'installation de Node.js a `u{e9}chou`u{e9}."; exit 1 }; $$installes += 'Node.js' }; if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { winget install --id Docker.DockerDesktop -e --silent --accept-package-agreements --accept-source-agreements; if ($$LASTEXITCODE -ne 0) { Write-Host "L'installation de Docker Desktop a `u{e9}chou`u{e9}."; exit 1 }; $$installes += 'Docker Desktop' }; if ($$installes) { $$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User'); Write-Host "Install`u{e9} : $$($$installes -join ', ')" };
NETTOYER = $$dll = '$(CLI)/bin/Release/net10.0/usine.dll'; if (Test-Path -LiteralPath $$dll) { try { [IO.File]::Open($$dll, 'Open', 'ReadWrite', 'None').Dispose() } catch { Write-Host "L'usine est en marche : arr`u{ea}te-la (Ctrl+C), puis relance make install."; exit 1 } }; Get-ChildItem -LiteralPath '$(ICI)/src' -Directory | ForEach-Object { Remove-Item -LiteralPath (Join-Path $$_.FullName 'bin'), (Join-Path $$_.FullName 'obj') -Recurse -Force -ErrorAction SilentlyContinue }; foreach ($$d in 'node_modules', 'dist', '.angular') { Remove-Item -LiteralPath "$(ICI)/front/$$d" -Recurse -Force -ErrorAction SilentlyContinue };
CONSTRUIRE_TOUT = dotnet build '$(CLI)' -c Release; if ($$LASTEXITCODE -ne 0) { exit 1 }; Push-Location -LiteralPath '$(ICI)/front'; npm ci; if ($$LASTEXITCODE -ne 0) { exit 1 }; npm run build; if ($$LASTEXITCODE -ne 0) { exit 1 }; Pop-Location;
DEMARRER_DOCKER = docker version --format '{{.Server.Version}}' *> $$null; if ($$LASTEXITCODE -ne 0) { $$dd = Join-Path $$env:ProgramFiles 'Docker\Docker\Docker Desktop.exe'; if (Test-Path -LiteralPath $$dd) { Write-Host "D`u{e9}marrage de Docker Desktop`u{2026}"; explorer.exe $$dd; $$limite = (Get-Date).AddMinutes(5); do { Start-Sleep -Seconds 5; docker version --format '{{.Server.Version}}' *> $$null } while ($$LASTEXITCODE -ne 0 -and (Get-Date) -lt $$limite) }; if ($$LASTEXITCODE -ne 0) { Write-Host "Docker ne r`u{e9}pond pas : d`u{e9}marre Docker Desktop, attends qu'il soit pr`u{ea}t, puis relance make install."; exit 1 } };
EXIGER = $(if $($(1)),,$(error $(1) manquant : $(2)))

.DEFAULT_GOAL := aide
.PHONY: install start start-auto interface run aide lancer suivre statut detail liste repondre valider refuser rapport livrable essayer reprendre reevaluer annuler diagnostic

install: ## réinstalle tout de zéro : logiciels manquants (winget), dépendances, usine, écran et image Docker du bac à sable, puis diagnostic
	@$$ErrorActionPreference = 'Stop'; $(INSTALLER_LOGICIELS) $(NETTOYER) $(CONSTRUIRE_TOUT) $(DEMARRER_DOCKER) & '$(USINE)' image --forcer; if ($$LASTEXITCODE -ne 0) { exit 1 }; & '$(USINE)' diagnostic; Write-Host "Installation termin`u{e9}e. Lance make run, puis choisis une IA et colle ta cl`u{e9} dans Param`u{e8}tres."

start: ## [MODELE=fournisseur:modèle] : point d'entrée unique, parle à l'usine comme à une personne, tout défile en direct (/quitter pour sortir)
	@$(PREPARER) $$a = @('chat'); if ($$env:MODELE) { $$a += @('--modele', $$env:MODELE) }; & '$(USINE)' @a

start-auto: ## [MODELE=fournisseur:modèle] : comme start, mais chaque demande est en mode auto après le backlog (tu réponds aux questions du PO, valides le besoin qu'il a compris puis le backlog, l'usine fait le reste seule)
	@$(PREPARER) $$a = @('chat', '--auto-apres-backlog'); if ($$env:MODELE) { $$a += @('--modele', $$env:MODELE) }; & '$(USINE)' @a

interface: ## [PORT=<port>] : construit l'écran si besoin (Node.js nécessaire), puis démarre l'interface web de l'usine, une API sur http://localhost:5180 par défaut, joignable de ce poste seulement (Ctrl+C pour arrêter)
	@$(PREPARER) $(CONSTRUIRE_ECRAN) $$a = @('interface'); if ($$env:PORT) { $$a += @('--port', $$env:PORT) }; & '$(USINE)' @a

run: interface ## [PORT=<port>] : tout lancer pour essayer l'interface web, raccourci de make interface (construit l'usine et l'écran si besoin, démarre l'interface et ouvre le navigateur sur son adresse à jeton ; Ctrl+C pour arrêter)

aide: ## affiche cette aide
	@Get-Content -Encoding utf8 -LiteralPath '$(CE_MAKEFILE)' | ForEach-Object { if ($$_ -match '^([a-z-]+):.*?## (.*)$$') { 'make {0,-18} {1}' -f $$Matches[1], $$Matches[2] } }; Write-Host "USINE = $(USINE)"

lancer: ## DEMANDE="..." [DEPOT=C:\chemin\depot] [TITRE="..."] : lance une demande (DEPOT fait évoluer un dépôt git)
	$(call EXIGER,DEMANDE,make lancer DEMANDE="...")
	@$(PREPARER) $$a = @('lancer', $$env:DEMANDE); if ($$env:DEPOT) { $$a += @('--depot', $$env:DEPOT) }; if ($$env:TITRE) { $$a += @('--titre', $$env:TITRE) }; & '$(USINE)' @a

suivre: ## ID=<id> : chronologie en direct (Ctrl+C pour quitter, la demande continue)
	$(call EXIGER,ID,make suivre ID=<id>)
	@$(PREPARER) & '$(USINE)' suivre $$env:ID

statut: ## ID=<id> : étape en cours et action attendue
	$(call EXIGER,ID,make statut ID=<id>)
	@$(PREPARER) & '$(USINE)' statut $$env:ID

detail: ## ID=<id> : statut détaillé (backlog, tests, résultats, coûts)
	$(call EXIGER,ID,make detail ID=<id>)
	@$(PREPARER) & '$(USINE)' statut $$env:ID --detail

liste: ## toutes les demandes
	@$(PREPARER) & '$(USINE)' liste

repondre: ## ID=<id> REPONSES="1. ... 2. ..." : répond aux questions du PO
	$(call EXIGER,ID,make repondre ID=<id> REPONSES="...")
	$(call EXIGER,REPONSES,make repondre ID=<id> REPONSES="...")
	@$(PREPARER) & '$(USINE)' repondre $$env:ID $$env:REPONSES

valider: ## ID=<id> ETAPE=besoin|backlog|tests|livraison : approuve l'étape
	$(call EXIGER,ID,make valider ID=<id> ETAPE=besoin|backlog|tests|livraison)
	$(call EXIGER,ETAPE,make valider ID=<id> ETAPE=besoin|backlog|tests|livraison)
	@$(PREPARER) & '$(USINE)' valider $$env:ID $$env:ETAPE

refuser: ## ID=<id> ETAPE=besoin|backlog|tests|livraison MOTIF="..." : refuse l'étape, le motif est transmis au rôle relancé
	$(call EXIGER,ID,make refuser ID=<id> ETAPE=besoin|backlog|tests|livraison MOTIF="...")
	$(call EXIGER,ETAPE,make refuser ID=<id> ETAPE=besoin|backlog|tests|livraison MOTIF="...")
	$(call EXIGER,MOTIF,make refuser ID=<id> ETAPE=besoin|backlog|tests|livraison MOTIF="...")
	@$(PREPARER) & '$(USINE)' valider $$env:ID $$env:ETAPE --refuser $$env:MOTIF

rapport: ## ID=<id> : rapport complet de la livraison
	$(call EXIGER,ID,make rapport ID=<id>)
	@$(PREPARER) & '$(USINE)' rapport $$env:ID

livrable: ## ID=<id> : ouvre le dossier du code livré
	$(call EXIGER,ID,make livrable ID=<id>)
	@$(PREPARER) $$d = Join-Path (& '$(USINE)' config donnees) "demandes/$$env:ID/livrable/code"; if (Test-Path -LiteralPath $$d) { Write-Host $$d; Invoke-Item -LiteralPath $$d } else { Write-Host "Pas encore de livrable : $$d"; exit 1 }

essayer: ## PROJET=<nom> | ID=<id> [PORT=<port>] : lance dans Docker un projet de l'usine ou le livrable d'une demande (http://localhost:8080 par défaut, Ctrl+C pour arrêter)
	@$(PREPARER) $$a = @('essayer'); if ($$env:PROJET) { $$a += @('--projet', $$env:PROJET) }; if ($$env:ID) { $$a += @($$env:ID) }; if ($$env:PORT) { $$a += @('--port', $$env:PORT) }; & '$(USINE)' @a

reprendre: ## ID=<id> : reprend une demande échouée à l'étape de son échec
	$(call EXIGER,ID,make reprendre ID=<id>)
	@$(PREPARER) & '$(USINE)' reprendre $$env:ID

reevaluer: ## ID=<id> [NOUVEAUX_TESTS=1] : réévalue la livraison d'une demande terminée dans une nouvelle demande (mode auto, livrable non publié ; NOUVEAUX_TESTS : le PO est relancé pour les seules notions, le QA amont réécrit les tests sans voir le code du livrable, puis vérification directe du livrable)
	$(call EXIGER,ID,make reevaluer ID=<id>)
	@$(PREPARER) $$a = @('reevaluer', $$env:ID); if ($$env:NOUVEAUX_TESTS -eq '1') { $$a += '--nouveaux-tests' }; & '$(USINE)' @a

annuler: ## ID=<id> : arrête une demande et supprime son bac à sable
	$(call EXIGER,ID,make annuler ID=<id>)
	@$(PREPARER) & '$(USINE)' annuler $$env:ID

diagnostic: ## vérifie Docker, la configuration et les modèles
	@$(PREPARER) & '$(USINE)' diagnostic

