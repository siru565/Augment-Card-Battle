@echo off
rem 개발용: tools\commit_msg.txt 내용으로 지금 브랜치에 커밋합니다. (push는 하지 않습니다)
cd /d "%~dp0.."
if exist .git\index.lock del .git\index.lock
git add -A
git commit -F tools\commit_msg.txt
git log --oneline -1
