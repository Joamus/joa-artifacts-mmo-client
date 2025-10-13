#!/bin/bash
char="$1"
code="$2"

if [ -z "$char" ] || [ -z "$code" ]; then
  echo "Usage: $0 <CharacterName> <ItemCode>"
  exit 1
fi

body='{ "Code": '"\"$code\""', "ForBank": false }'

echo $body

url="http://localhost:8080/char/$char/job/obtainItem" 
	curl -X POST $url \
			-H "Content-Type: application/json" \
			-d "$body" \
			-kv
