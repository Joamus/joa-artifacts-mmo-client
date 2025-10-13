chars=("Leonidas" "Ramsey" "Pagle" "Beatrix" "Milly")

body='{ "Relative": true, "Level": 1, "Idle": true }'

for char in "${chars[@]}";
	do
	url="http://localhost:8080/char/$char/train/combat" 
	echo $url
		curl -X POST $url \
				-H "Content-Type: application/json" \
				-d "$body" \
				-kv
	done
