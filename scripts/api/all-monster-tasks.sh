# Do item tasks for all characters
chars=("Leonidas" "Ramsey" "Pagle" "Beatrix" "Milly")

body='{ "Idle": true, "ForBank": true }'

for char in "${chars[@]}";
	do
		url="http://localhost:8080/char/$char/monster/item" 
		echo $url
			curl -X POST $url \
					-H "Content-Type: application/json" \
					-d "$body" \
					-kv
	done


