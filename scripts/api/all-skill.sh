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