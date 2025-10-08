chars=["Leonidas", "Ramsey", "Pagle", "Beatrix", "Milly"]

for CHAR in chars:
	do
		curl -X POST "http://localhost:8080/char/$CHAR/train/combat" \
				-H "Content-Type: application/json" \
				-d '{ "Relative": true, "Level": 1, "Idle": true }' \
				-k
	done
