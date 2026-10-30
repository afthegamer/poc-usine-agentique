const prevision = (nombre: number, ajout: boolean) =>
  ajout
    ? `Parfait. Pour cet ajout, je prévois ${nombre === 1 ? 'un changement' : `${nombre} changements`}.`
    : `Parfait. Ton projet permettra de faire ${nombre === 1 ? 'une chose' : `${nombre} choses`}.`;

const descriptionsModeles: Readonly<Record<string, string>> = {
  'gemini-3.8-flash': 'Rapide et économique. Convient à la plupart des projets.',
  'gemini-3.1-pro-preview': 'Plus puissant, plus lent et plus cher.',
  'claude-haiku-5-5': 'Rapide et économique.',
  'claude-sonnet-5-5': 'Équilibré. Convient à la plupart des projets.',
  'claude-opus-5-5': 'Le plus puissant, plus lent et plus cher.',
};

const qualificatifsModeles: Readonly<Record<string, string>> = {
  'gemini-3.8-flash': 'rapide',
  'gemini-3.1-pro-preview': 'puissant',
  'claude-haiku-5-5': 'rapide',
  'claude-sonnet-5-5': 'équilibré',
  'claude-opus-5-5': 'le plus puissant',
};

export const libelles = {
  marque: 'Usine',
  titreOnglet: (attend: boolean) => (attend ? '(ta réponse) Usine' : 'Usine'),
  connexionCoupee: "La connexion à l'usine est coupée. Je réessaie…",
  jours: {
    aujourdhui: "Aujourd'hui",
    hier: 'Hier',
  },
  erreurs: {
    docker:
      "L'usine a besoin de Docker Desktop : démarre-le, attends qu'il soit prêt, puis réessaie.",
    image:
      "L'usine n'est pas encore prête à faire tourner les applications sur ce poste. Demande à la personne qui l'a installée de la préparer.",
    siteNonDemarre: "Le site n'a pas pu démarrer : réessaie dans un instant.",
    siteNonFerme: "Le site n'a pas pu être fermé : réessaie dans un instant.",
    ajoutEnCours: "Ce projet a déjà un ajout en cours : attends qu'il soit fini, ou annule-le.",
    arretEnCours: "L'usine termine de s'arrêter : réessaie dans quelques secondes.",
    repriseImpossible:
      "Ce projet ne peut plus reprendre là où il s'est arrêté. Tu peux l'annuler et recommencer.",
    projetEnregistre:
      "Ton projet est enregistré, mais l'usine n'a pas pu se mettre au travail. Rouvre-le dans un moment depuis « Tes projets ».",
    reponsesEnregistrees:
      "Tes réponses sont bien enregistrées, mais l'usine n'a pas pu se remettre au travail. Rouvre le projet dans un moment.",
    avisEnregistre:
      "Ton avis est bien enregistré, mais l'usine n'a pas pu se remettre au travail. Rouvre le projet dans un moment.",
    messageVide: "Écris d'abord ton message.",
    projetDisparu: "Ce projet n'existe plus.",
    sansReponse: "L'usine ne répond pas. Vérifie qu'elle est toujours lancée, puis réessaie.",
    nonPrisEnCompte:
      "Ça n'a pas pu être pris en compte. Réessaie, ou dis-le à l'usine avec tes mots.",
    pageRefusee: "L'usine refuse cette page. Rouvre-la depuis l'adresse affichée à son lancement.",
    aJour: "L'usine a avancé entre-temps : l'écran est à jour.",
    probleme: "Un problème est survenu dans l'usine. Réessaie dans un moment.",
    discussionImpossible: "L'usine ne peut pas discuter pour le moment. Réessaie dans un moment.",
  },
  initialeUsine: 'U',
  toi: 'Toi',
  signature: (auteur: string, heure: string | null) => (heure ? `${auteur} · ${heure}` : auteur),
  commandeImpossible: (texte: string) => `« ${texte} » n'est pas une commande possible ici.`,
  commandes: {
    titre: 'Ce que tu peux faire ici',
    liste: 'Commandes possibles ici',
    aucuneIci: 'Aucune commande ici.',
    aucuneQuiCommencePar: (mot: string) => `Aucune commande ne commence par « ${mot} ».`,
    pied: 'Flèches pour choisir · Tab ou clic pour compléter · Entrée pour envoyer · Échap pour fermer',
    descriptions: {
      nouveau: 'Commencer un nouveau projet',
      ouvrir: 'Ouvrir ton site',
      reprendre: "Reprendre là où ça s'est arrêté",
    },
  },
  titres: {
    accueil: 'Nouveau projet',
    applications: 'Mes applications',
    application: 'Ton application',
  },
  situations: {
    annulee: { mot: '', description: '', court: 'annulé' },
    annulation: {
      mot: 'annulation…',
      description: 'annulation en cours',
      court: 'annulation en cours',
    },
    arret: {
      mot: 'arrêté',
      description: 'arrêté, à reprendre',
      court: 'arrêté',
    },
    questions: {
      mot: 'ta réponse',
      description: 'attend ta réponse',
      court: 'en cours',
    },
    compris: {
      mot: 'ta réponse',
      description: 'attend ta réponse',
      court: 'en cours',
    },
    plan: {
      mot: 'ta réponse',
      description: 'attend ta réponse',
      court: 'en cours',
    },
    'attente-externe': {
      mot: 'à valider',
      description: "à valider hors de l'interface",
      court: 'en cours',
    },
    preparation: {
      mot: 'en préparation',
      description: 'en préparation',
      court: 'en cours',
    },
    fabrication: {
      mot: 'en fabrication',
      description: 'en fabrication',
      court: 'en fabrication',
    },
    pret: { mot: '', description: '', court: 'terminé' },
    terminee: { mot: '', description: '', court: 'terminé' },
    inabouti: {
      mot: "n'a pas abouti",
      description: "n'a pas abouti",
      court: "n'a pas abouti",
    },
    inconnue: { mot: '', description: '', court: '' },
  },
  accueil: {
    etiquette: 'Décris ton projet',
    indication: 'Par exemple : un site pour vendre des chaussures en ligne.',
    question: 'Que veux-tu créer ?',
    aide: "Décris ton idée avec tes mots. Je te pose quelques questions si besoin, puis je te montre ce que j'ai compris avant de fabriquer quoi que ce soit.",
    panneau: 'Il prend forme ici pendant la discussion\u00a0: son nom, ses étapes, puis ton site.',
    sansIa: {
      titre: "Connecte d'abord une IA",
      phrase:
        "J'ai besoin d'une IA pour comprendre ta demande et fabriquer ton projet. Ça prend une minute.",
      connecter: 'Connecter une IA',
      indication: "Disponible dès qu'une IA est connectée.",
    },
  },
  applications: {
    phrase: "Tes applications prêtes à l'emploi. Clique sur l'une d'elles pour l'utiliser.",
    pied: {
      avantLeLien:
        "Un projet apparaît ici dès qu'il est prêt à utiliser. Pour en créer un ou en modifier un, passe par l'",
      lien: 'accueil',
      apresLeLien: '.',
    },
    aucune: "Aucune application pour l'instant. Crée ton premier projet depuis l'accueil.",
  },
  application: {
    modifier: "Modifier avec l'usine",
    relancer: 'Relancer mon site',
    fermer: 'Fermer mon site',
    nouvelOnglet: 'Ouvrir dans un nouvel onglet',
    demarrage: 'Je démarre ton site… La première fois, cela peut prendre quelques minutes.',
    disparu: "Ton site n'a pas pu démarrer.",
    lent: 'Ton site met beaucoup de temps à démarrer.',
    reessayer: 'Réessayer',
  },
  parametres: {
    titre: 'Paramètres',
    phrasePrete: "Choisis l'IA avec laquelle l'usine travaille.",
    phrase:
      "Choisis l'IA avec laquelle l'usine travaille, puis colle sa clé. Tu pourras en connecter d'autres ensuite.",
    echec: "Les paramètres n'ont pas pu être lus. Réessaie dans un instant.",
    reessayer: 'Réessayer',
    connecte: (nom: string) => `${nom} est connecté`,
    utilisera: (modele: string) =>
      `L'usine utilisera ${modele}. Tu peux changer de modèle ci-dessous quand tu veux.`,
    premierProjet: 'Créer mon premier projet',
    tonIa: 'Ton IA',
    ias: {
      gemini: { nom: 'Gemini', description: 'Les modèles de Google.' },
      anthropic: { nom: 'Anthropic', description: 'Les modèles Claude.' },
      autre: {
        nom: 'Autre service',
        description: 'Mistral, Groq et les services compatibles OpenAI.',
      },
      ollama: {
        nom: 'Ollama',
        description: 'Les modèles installés sur ce poste. Gratuit, sans clé.',
      },
    },
    services: {
      mistral: 'Mistral',
      groq: 'Groq',
      openrouter: 'OpenRouter',
      adresse: 'Autre adresse',
    },
    connectee: 'Connecté',
    cleAAjouter: 'Clé à ajouter',
    detecte: 'Détecté sur ce poste',
    introuvable: 'Introuvable sur ce poste',
    cleCreee:
      "Une clé d'accès se crée sur le site de l'IA choisie, dans ton compte. Elle est chiffrée avant d'être enregistrée et n'est jamais réaffichée.",
    taCle: (nom: string) => `1. Ta clé ${nom}`,
    indicationCle: 'Colle ta clé ici',
    enregistrerCle: 'Enregistrer la clé',
    verifie: (nom: string) => `L'usine vérifie la clé auprès de ${nom} avant de l'enregistrer.`,
    leModele: '2. Le modèle',
    disponible: 'Disponible dès que la clé est enregistrée.',
    cleAcces: "Clé d'accès",
    cleEnregistree: 'Clé enregistrée',
    chiffree: "Elle est chiffrée et n'est jamais réaffichée.",
    clePoste: 'Clé fournie par la configuration du poste.',
    remplacer: 'Remplacer la clé',
    annuler: 'Annuler',
    retirer: 'Retirer',
    modele: 'Modèle',
    description: (modele: string): string => descriptionsModeles[modele] ?? '',
    autre: {
      etapes: 'Trois étapes : le service, sa clé, puis le modèle.',
      service: '1. Le service',
      connus:
        "L'usine connaît déjà l'adresse de ces services. « Autre adresse » demande l'adresse du service, par exemple celle d'un modèle installé sur ton serveur.",
      adresse: 'Adresse du service',
      cle: (nom: string) => `2. La clé d'accès ${nom}`,
      etiquetteCle: (nom: string) => `Clé ${nom}`,
      seraChiffree: 'Elle est chiffrée et ne sera jamais réaffichée.',
      modele: '3. Le modèle',
      sansCle: "Enregistre d'abord la clé",
      choisir: 'Choisis un modèle',
      liste: (nom: string) =>
        `Dès que la clé est enregistrée, l'usine demande à ${nom} la liste de ses modèles et te la propose ici.`,
    },
    ollama: {
      surCePoste: 'Sur ce poste',
      repond: 'Ollama répond',
      sansCle:
        'Aucune clé à donner : les modèles tournent sur ta machine, sans frais. Ils sont en général plus lents et moins forts que ceux de Gemini ou de Claude.',
      neRepondPas: 'Ollama ne répond pas sur ce poste.',
      lancer: 'Lance Ollama, puis reviens sur cette page.',
      changerAdresse: "Changer l'adresse",
      adresse: "Adresse d'Ollama",
      enregistrerAdresse: "Enregistrer l'adresse",
      installes: 'Les modèles installés dans Ollama.',
      prepare: "Préparé pour l'usine",
      preparation: 'Préparation…',
      premiereFois:
        "La première fois que tu choisis un modèle, l'usine le prépare pour qu'il garde tout le fil d'une tâche. Ça prend quelques secondes.",
      aucunModele: "Aucun modèle n'est installé dans Ollama.",
    },
    note: "Ton choix vaut pour ce que l'usine lance ensuite ; ce qui tourne déjà garde son modèle.",
    avances: 'Paramètres avancés',
    avancesPhrase: 'Un modèle différent pour comprendre ta demande, fabriquer ou vérifier.',
    retrait: {
      titre: (nom: string) => `Retirer la clé ${nom} ?`,
      versAutre: (nom: string, modele: string) =>
        `L'usine ne pourra plus utiliser ${nom}. Les rôles réglés sur un modèle ${nom} passeront sur ${modele}, le modèle de ton autre IA connectée.`,
      sansAutre: (nom: string) =>
        `L'usine ne pourra plus utiliser ${nom}. Il faudra connecter une autre IA avant de créer.`,
      oui: 'Oui, retirer',
      non: 'Non, garder la clé',
    },
  },
  parametresAvances: {
    retour: 'Paramètres',
    titre: 'Un modèle par rôle',
    phrase: (modele: string) =>
      `Chaque rôle peut utiliser le modèle de ton choix, de n'importe quelle IA connectée. Par défaut, il suit ton choix général : ${modele}.`,
    roles: {
      po: {
        nom: 'Comprendre ta demande',
        description:
          'Pose les questions, résume le besoin et écrit le plan. Répond aussi dans la discussion.',
      },
      qaAmont: {
        nom: 'Préparer les tests',
        description: 'Écrit, avant la fabrication, les tests qui diront si chaque fonction marche.',
      },
      dev: { nom: 'Fabriquer', description: 'Écrit le code de chaque fonction.' },
      qa: {
        nom: 'Vérifier',
        description: 'Relance les tests et contrôle le résultat avant de te le livrer.',
      },
    },
    personnalise: 'Personnalisé',
    commeGeneral: (modele: string) => `Comme le choix général (${modele})`,
    ollama: 'Ollama · sur ce poste',
    cleAAjouter: (nom: string) => `${nom} · clé à ajouter`,
    ajouteCle: 'Ajoute sa clé dans Paramètres',
    option: (nom: string, modele: string) => {
      const qualificatif = qualificatifsModeles[modele];
      return qualificatif === undefined ? nom : `${nom} · ${qualificatif}`;
    },
    pied: 'Seules les IA connectées sont proposées. Pour en ajouter une, ajoute sa clé dans',
    lienParametres: 'Paramètres',
    toutRemettre: 'Tout remettre sur le choix général',
    sansChoix: "Choisis d'abord ton IA dans",
  },
  colonne: {
    navigation: 'Projets',
    nouveauProjet: '+ Nouveau projet',
    titre: 'Tes projets',
    titreReplie: (nombre: number) => `Tes projets (${nombre})`,
    aucunProjet: 'Pas encore de projet.',
    parametres: 'Paramètres',
  },
  panneau: {
    intitule: 'Ton projet',
    ajout: (titre: string) => `Ajout : ${titre}`,
    progression: (rang: number, total: number) => ` · ${rang} sur ${total}`,
    siteAVenir: "Ton site sera ici dès qu'il sera prêt.",
    siteAOuvrir: 'Ouvre ton site pour le voir.',
    siteOuvert: 'Ton site est ouvert.',
    siteActuel:
      "Ton site actuel reste utilisable pendant l'ajout. Il n'est remplacé qu'une fois la nouvelle version vérifiée.",
    siteActuelFabrication: 'Ton site actuel reste utilisable pendant la fabrication.',
    pret: 'Prêt à utiliser',
    ouvrirSite: 'Ouvrir mon site',
    demanderModification: 'Demander une modification',
    historique: 'Historique',
    ligneHistorique: (titre: string, etat: string, jour: string) =>
      etat ? `${titre}, ${etat} · ${jour}` : `${titre} · ${jour}`,
    annuler: (ajout: boolean) => (ajout ? 'Annuler cet ajout' : 'Annuler ce projet'),
    siteIntact: "Ton site actuel n'est pas touché.",
  },
  attentes: {
    reponse: "L'usine attend ta réponse pour continuer.",
    avis: (derniere: boolean, ajout: boolean) => {
      const sujet = ajout
        ? derniere
          ? 'le dernier changement'
          : 'ce changement'
        : derniere
          ? 'la dernière fonction'
          : 'cette fonction';
      return `L'usine attend ton avis sur ${sujet}.`;
    },
    preparation: "L'usine prépare la suite. Rien à faire de ton côté.",
    fabrication: 'Rien à faire de ton côté : cette page se met à jour toute seule.',
    arret: "L'usine s'est arrêtée : elle attend que tu la relances.",
    annulation: 'Annulation en cours…',
    externe: "Cette étape se valide hors de l'interface.",
  },
  conversation: {
    reessayer: 'Réessayer',
    retourAccueil: "Retour à l'accueil",
    indicationQuestion: "Pose une question à l'usine…",
    indicationModification: 'Demande une modification, ou pose une question…',
    lectureDemande: 'Je lis ta demande…',
    preparationPlan: 'Je prépare le plan…',
    arretEnCours: "J'arrête…",
    travailEnCours: 'Je travaille sur ta demande…',
    projetAnnule: 'Ce projet a été annulé.',
    ajoutAnnule: "L'ajout est annulé. Ton site n'a pas changé.",
    validationAilleurs:
      "Cette demande a été lancée en dehors de l'interface : sa validation se fait là où elle a été lancée. Tu peux aussi en parler à l'usine ci-dessous.",
  },
  fabrication: {
    depart: (ajout: boolean) =>
      ajout
        ? "C'est parti ! Je fabrique ton ajout. Pendant ce temps, ton site actuel reste utilisable : il ne sera remplacé qu'une fois la nouvelle version vérifiée. Cette page se met à jour toute seule."
        : "C'est parti ! Je fabrique ton projet. Cette page se met à jour toute seule : tu peux aussi la fermer et revenir plus tard.",
    sousEtapes: [
      {
        faite: 'Préparation des vérifications',
        enCours: 'Préparation des vérifications',
        aVenir: 'Préparation des vérifications',
      },
      { faite: 'Fabrication', enCours: 'Fabrication en cours', aVenir: 'Fabrication' },
      {
        faite: 'Vérifications',
        enCours: 'Vérifications en cours',
        aVenir: 'Vérifications à venir',
      },
    ],
    verificationsAjout: (libelle: string) => `${libelle} : ton ajout, et tout ce qui marchait déjà`,
  },
  pret: {
    reussi: (ajout: boolean) =>
      ajout
        ? "Ton ajout est prêt ! Ton site a été mis à jour, et j'ai vérifié que tout ce qui marchait déjà marche encore."
        : "Ton projet est prêt ! J'ai vérifié que tout ce qu'on avait prévu fonctionne. Tu le retrouves aussi dans « Mes applications », pour l'utiliser sans repasser par cette discussion.",
    disponible: (ajout: boolean) =>
      ajout
        ? 'Ton ajout est en place, mais certaines vérifications ne passent pas.'
        : 'Ton projet est disponible, mais certaines vérifications ne passent pas.',
    echec: (ajout: boolean) =>
      ajout
        ? "Je n'ai pas réussi à fabriquer ton ajout : certaines vérifications ne passent pas. Ton site n'a pas changé."
        : "Je n'ai pas réussi à fabriquer ton projet : certaines vérifications ne passent pas. Il n'a pas été mis dans « Mes applications ».",
    nonPublie: (ajout: boolean) =>
      ajout
        ? "Ton ajout est fabriqué, mais je n'ai pas pu le mettre à disposition : ton site n'a pas changé."
        : "Ton projet est fabriqué, mais je n'ai pas pu le mettre à disposition.",
    sansDemande: 'Ton projet est prêt à être utilisé.',
    recommencer: 'Recommencer',
    remarques: (nombre: number) => `Remarques de la vérification (${nombre})`,
  },
  arret: {
    message: (phase: string | null) =>
      `Je me suis arrêté sur un problème${phase === null ? '' : ` pendant ${phase}`}. Tu peux me relancer : je reprendrai là où je me suis arrêté.`,
    phases: {
      comprendre: 'la compréhension de ta demande',
      plan: 'la préparation du plan',
      fabrication: 'la fabrication',
      pret: 'la mise à disposition de ton projet',
    },
    reprendre: 'Reprendre',
  },
  fil: {
    reponsesDonnees: "J'ai répondu aux questions.",
    besoinApprouve: "Oui, c'est ça.",
    besoinRefuse: 'Pas tout à fait :',
    planRefuse: 'Je veux changer ces fonctions :',
    fonction: (numero: string) => `Fonction ${numero} :`,
    planValide: prevision,
    planApprouve: "D'accord, lance la fabrication.",
    reprise: "Reprends là où ça s'est arrêté.",
    annulation: (ajout: boolean) => (ajout ? 'Annule cet ajout.' : 'Annule ce projet.'),
    demandeAnnulee: 'Cette demande a été annulée.',
    nouvelAjout: (jour: string) => `Nouvel ajout · ${jour}`,
    recapitulatif: (ajout: boolean, pret: boolean) =>
      `${ajout ? 'Ajout compris' : 'Demande comprise'} · plan validé${pret ? ' · fabrication · vérifications' : ''}`,
  },
  revoir: {
    ligne: (titre: string, jour: string) => `${titre}, le ${jour}`,
    bouton: 'Revoir la discussion',
  },
  questions: {
    introduction: (nombre: number, ajout: boolean) => {
      const debut = ajout ? 'Avant de préparer ton ajout' : 'Avant de continuer';
      return nombre === 1
        ? `${debut}, j'ai une question.`
        : `${debut}, j'ai ${nombre} questions. Je te les pose une par une.`;
    },
    numero: (rang: number) => `Question ${rang}`,
    position: (rang: number, total: number) => `Question ${rang} sur ${total}`,
    sansPreference: 'Pas de préférence, choisis au mieux',
    reponseVide: 'Pas de préférence',
    precedente: 'Question précédente',
    envoi: "J'envoie tes réponses…",
    etiquette: 'Ta réponse',
    indication: 'Écris ta réponse…',
  },
  compris: {
    introduction: (ajout: boolean) =>
      ajout
        ? "J'ai regardé ton projet tel qu'il est aujourd'hui. Voici ce que j'ai compris de ton ajout. Dis-moi si c'est bien ça."
        : "Avant de préparer le plan, voici ce que j'ai compris. Dis-moi si c'est bien ça.",
    oui: "Oui, c'est ça",
    non: 'Pas tout à fait',
    indication: "Dis-moi ce qu'il faut corriger…",
    indicationDiscussion: "Dis-moi si c'est bien ça, ou pose une question…",
  },
  plan: {
    introduction: (nombre: number, ajout: boolean) =>
      `${prevision(nombre, ajout)} ${ajout ? "Je te les présente un par un : dis-moi pour chacun s'il te va." : 'Je te les présente une par une : dis-moi pour chacune si elle te va.'}`,
    position: (rang: number, total: number) => `${rang} sur ${total}`,
    dejaVu: 'Déjà vu',
    daccord: "D'accord",
    lancer: "D'accord, lance la fabrication",
    envoyerRemarques: 'Envoyer mes remarques',
    changer: 'Je veux changer quelque chose',
    precedente: 'Fonction précédente',
    accord: (heure: string) => `d'accord · ${heure}`,
    aChanger: (heure: string) => `à changer · ${heure}`,
    indication: 'Dis-moi ce que tu veux changer…',
    indicationDiscussion: 'Dis-moi si ça te va, ou pose une question…',
  },
  annulation: {
    titre: (ajout: boolean) => (ajout ? 'Annuler cet ajout ?' : 'Annuler ce projet ?'),
    phrase: (ajout: boolean) =>
      `L'usine arrête tout de suite. Ce qui a été fait pour cette demande sera perdu.${ajout ? " Ton site actuel n'est pas touché." : ''}`,
    oui: 'Oui, annuler',
    non: 'Non, continuer',
    projetAnnule: (titre: string) => `Le projet « ${titre} » est annulé.`,
    tropTard: 'Trop tard : la demande était déjà terminée.',
  },
  etapes: {
    liste: ['Comprendre ta demande', 'Valider le plan', 'Fabrication', 'Prêt à utiliser'],
    faite: 'faite',
  },
  saisie: {
    message: 'Ton message',
    envoyer: 'Envoyer',
  },
  discussion: {
    reflechit: "L'usine réfléchit…",
    sEnOccupe: "L'usine s'en occupe…",
    sansReponse: "Je n'ai pas su répondre. Reformule ton message, s'il te plaît.",
    erreur: "Je n'ai pas pu répondre à ce message. Réessaie dans un moment.",
    nouvelle: "Nouvelle conversation avec l'usine",
    oui: 'Oui',
    non: 'Non',
    questions: {
      avis: 'Je peux enregistrer ton avis sur cette étape ?',
      annulation: 'Je peux annuler cette demande ? Ce qui a été fait pour elle sera perdu.',
      reprise: "Je peux reprendre la fabrication là où elle s'est arrêtée ?",
      autre: 'Je peux faire cette action ? Elle ne pourra pas être défaite.',
    },
  },
  accesRefuse: {
    titre: "L'accès à l'usine a expiré.",
    consigne:
      "Rouvre l'usine depuis l'adresse affichée par « usine interface » à son lancement. Si tu viens de la relancer, une nouvelle page s'est ouverte : utilise-la.",
  },
} as const;
