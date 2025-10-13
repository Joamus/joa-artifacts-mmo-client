# Train Alchemy for all
chars=("Leonidas" "Ramsey" "Pagle" "Beatrix" "Milly")

for char in "${chars[@]}";
	do
		url="http://localhost:8080/char/$char/train/skill" 
		echo $url
		curl -X POST $url \
				-H "Content-Type: application/json" \
				-d '{ "Skill": "Alchemy", "Relative": true, "Level": 1, "Idle": true }' \
				-kv
	done


