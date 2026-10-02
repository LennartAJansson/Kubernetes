# Om man vill köra nats utan ett kluster så kan man använda dessa verktyg:
# winget install -e --id NATSAuthors.NATSServer   # nats-server
# winget install -e --id NATSAuthors.CLI          # nats (CLI)
# Som standard kommer nats cli ansluta till localhost:4222 oavsett om den finns i klustret eller lokalt

# Jag har redan sparat en stream konfiguration i persons-stream.json med nedanstående kommando:
# natscli stream info PERSONS --json > persons-stream.json

# Om du inte har den så skapar du den med:
natscli stream add --config persons-stream.json