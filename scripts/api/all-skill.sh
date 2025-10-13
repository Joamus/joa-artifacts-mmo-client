curl -X POST "http://localhost:8080/char/Leonidas/train/skill" \
        -H "Content-Type: application/json" \
		-d '{ "Skill": "Weaponcrafting", "Relative": true, "Level": 1, "Idle": true }' \
		-k

curl -X POST "http://localhost:8080/char/Ramsey/train/skill" \
        -H "Content-Type: application/json" \
		-d '{ "Skill": "Gearcrafting", "Relative": true, "Level": 1, "Idle": true }' \
		-k

curl -X POST "http://localhost:8080/char/Pagle/train/skill" \
        -H "Content-Type: application/json" \
		-d '{ "Skill": "Jewelrycrafting", "Relative": true, "Level": 1, "Idle": true }' \
		-k

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


