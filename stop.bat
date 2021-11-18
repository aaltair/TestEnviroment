FOR /F "tokens=4 delims= " %%P IN ('netstat -a -n -o ^| findstr :5000') DO TaskKill.exe /PID %%P
