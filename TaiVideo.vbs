' Khoi dong Tai Video mà KHONG hien cua so console.
' Ly do can file nay: tren Windows 11, console mac dinh la Windows Terminal,
' no bo qua co -WindowStyle Hidden cua PowerShell nen van hien mot cua so den
' va chiem mot o tren thanh tac vu. Chay qua wscript voi window style = 0 thi
' tien trinh duoc tao o che do an ngay tu dau.
Option Explicit

Dim sh, fso, here, ps1, cmd
Set sh  = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

here = fso.GetParentFolderName(WScript.ScriptFullName)
ps1  = fso.BuildPath(here, "TaiVideo.ps1")

If Not fso.FileExists(ps1) Then
    MsgBox "Khong tim thay TaiVideo.ps1 trong:" & vbCrLf & here, 16, "Tai Video"
    WScript.Quit 1
End If

cmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File """ & ps1 & """"

' 0 = cua so an, False = khong cho doi tien trinh ket thuc
sh.Run cmd, 0, False
