# Do item tasks for all characters
chars=("Leonidas" "Ramsey" "Pagle" "Beatrix" "Milly")

for char in "${chars[@]}";
	do
		url="http://localhost:8080/char/$char/task/item" 
		echo $url;
			curl -X POST $url \
					-H "Content-Type: application/json" \
					-d '{ "Relative": true, "Amount": 1, "Idle": true, "ForBank":" true }' \
					-kv
	done


