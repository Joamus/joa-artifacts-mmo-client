char="$1"

if [ -z "$char" ]; then
  echo "Usage: $1 <CharacterName>"
  exit 1
fi

body='{}'

url="http://localhost:8080/char/$char/task/monster" 
echo $url

curl -X POST $url \
		-H "Content-Type: application/json" \
		-d "$body" \
		-kv


