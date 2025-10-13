#!/bin/bash
#chars=("Leonidas" "Ramsey" "Pagle" "Beatrix" "Milly")

char="$1"
code="$2"
craftBy="$3"

if [ -z "$char" ] || [ -z "$code" ] || [ -z "$craftBy" ]; then
  echo "Usage: $0 <CharacterName> <ItemCode> <CraftByCharacter>"
  exit 1
fi

body='{ "Code": '"\"$code\""', "CraftBy": '"\"$craftBy\""' }'

url="http://localhost:8080/char/$char/job/gatherMaterialsForCraftItem" 

echo $url
	curl -X POST $url \
			-H "Content-Type: application/json" \
			-d "$body" \
			-kv
