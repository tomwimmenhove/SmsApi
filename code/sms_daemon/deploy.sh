#!/bin/bash

DEPLOY_HOST_FILE=".deploy_host"

if [ ! -f "$DEPLOY_HOST_FILE" ]; then
  echo "$DEPLOY_HOST_FILE does not exist"
  exit 1
fi

DEPLOY_HOST=`cat $DEPLOY_HOST_FILE`

dotnet build --configuration=Release || exit

rsync -r --progress bin/Release/net7.0/ sms@$DEPLOY_HOST:/opt/dotnet/sms_daemon || exit

echo "Restarting services. Press ^C to cancel"
ssh -t sms@$DEPLOY_HOST -- "sudo systemctl restart smsdaemon.service"

