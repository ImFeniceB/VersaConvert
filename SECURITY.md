# Sicurezza

Per segnalare una vulnerabilità, apri una segnalazione privata tramite la funzione **Private vulnerability reporting** del repository GitHub. Non pubblicare dettagli sfruttabili in una issue aperta.

## Ambito

Sono particolarmente rilevanti:

- esecuzione di comandi tramite nomi o percorsi file;
- scrittura fuori dalla cartella scelta;
- sovrascrittura non richiesta del sorgente;
- dipendenze o binari di release compromessi.

VersaConvert non invia file o telemetria in rete. Lo script di build accede alla rete soltanto per scaricare FFmpeg quando non è già disponibile localmente.
