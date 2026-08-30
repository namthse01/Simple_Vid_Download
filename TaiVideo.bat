@echo off
rem Goi qua TaiVideo.vbs de KHONG hien cua so console den phia sau app.
rem (Chay thang powershell -WindowStyle Hidden khong an thua tren Windows 11
rem  vi console mac dinh la Windows Terminal, no bo qua co do.)
rem Muon khong thay nhay console mot cai nao thi mo thang TaiVideo.vbs.
start "" wscript.exe "%~dp0TaiVideo.vbs"
