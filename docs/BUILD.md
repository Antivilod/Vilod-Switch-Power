# Сборка установщика 0.8.2

Windows x64, PowerShell 7, .NET SDK 10, Inno Setup 6. Сборщик Inno Setup должен быть установлен из официального источника. Из корня:

```powershell
pwsh -NoProfile -File scripts/build-installer.ps1
```

Результат: `artifacts/release/VSP-0.8.2-Setup-x64.exe` и SHA256SUMS.txt. Сборка включает 103 теста ядра, публикует автономные app/monitor/service/setup и сохраняет лицензии runtime. Она не устанавливает приложение автоматически.

Только на чистой одноразовой Windows-машине с правами администратора:

```powershell
pwsh -NoProfile -File scripts/test-installer.ps1
```

Этот тест действительно устанавливает службу, применяет и восстанавливает режимы на тестовой Windows-машине, запускает окно и удаляет программу. Не используйте его на рабочем компьютере с установленной VSP. NVIDIA-оборудование он не подтверждает. GitHub Actions выполняет этот тест на отдельном Windows-runner. Артефакт загружается только после успешных проверок.

Дополнительная регрессия CPU-политик на одноразовом Windows-стенде:

```powershell
dotnet run --project tests/QuietPC.Windows.Tests -c Release -- artifacts/release/CPU-POLICY-VERIFICATION.json
```

Она создаёт временные схемы, проверяет AC/DC, дополнительные настройки при наличии, minimum, Turbo 0/1/2 и восстановление. Не запускайте этот интеграционный тест на рабочем компьютере вместо штатного приложения.

Ниже сохранена историческая инструкция исходного выпуска 0.8.0. В новом пакете служба имеет версию 0.4.3, а публичные пользовательские настройки отделены от прежней версии.

# Сборка из исходников

Требования: Windows x64, .NET SDK 10 (global.json допускает актуальный feature band), PowerShell 7. Для NuGet-restore требуется интернет. Не нужны личные ключи, платный сертификат и готовые vendor/DLL.

Из корня репозитория:

```powershell
dotnet restore src/QuietPC.App/QuietPC.App.csproj --configfile NuGet.public.config
dotnet restore tests/QuietPC.Tests --configfile NuGet.public.config
dotnet build tests/QuietPC.Tests -c Release --no-restore
dotnet tests/QuietPC.Tests/bin/Release/net10.0/QuietPC.Tests.dll
dotnet build src/QuietPC.App -c Release
dotnet build src/QuietPC.Monitor -c Release
dotnet build src/QuietPC.Service -c Release
```

Удобная сборка всех основных частей без установки:

```powershell
pwsh -NoProfile -File scripts/build-source.ps1
```

Скрипт проверяет ядро, восстанавливает пакеты из публичного источника, публикует app/monitor/service в `artifacts/source-build/`. Не запускает приложение или службу, не меняет питание, не требует администратора. Не меняйте политику Windows ради запуска скрипта; если она запрещает скрипты, выполните соответствующие dotnet-команды вручную.

Отдельные публикации:

```powershell
dotnet publish src/QuietPC.App -c Release -r win-x64 --self-contained true -o artifacts/source-build/app
dotnet publish src/QuietPC.Monitor -c Release -r win-x64 --self-contained true -o artifacts/source-build/monitor
dotnet publish src/QuietPC.Service -c Release -r win-x64 --self-contained true -o artifacts/source-build/service
```

Запуск интерфейса: `artifacts/source-build/app/QuietPC.exe`. Мониторинг ожидает помощник в соседней папке `monitor`. Рядом с приложением можно создать пустой `portable.flag`; пользовательские данные portable будут создаваться локально и не входят в репозиторий.

**Служба:** исходный проект имеет собственную версию 0.4.1. В исходном частном выпуске 0.8.0 использовалась ранее проверенная служба 0.3.8; её бинарник сознательно не опубликован. Исторические исходники находятся в `legacy/backend-0.3.8/`. Их наличие не доказывает побитовую воспроизводимость установленной службы. Сборка службы из этого репозитория требует отдельной интеграционной проверки восстановления питания, прав доступа, владельца SID и аварийного завершения. Она автоматически не устанавливается. Не подменяйте ею рабочую службу без испытаний.

Необязательные проект Launcher и тесты QuietPC.App.Tests сохранены в исходниках; UI-тесты требуют Windows и запуска исполняемого кода, допускаемого политикой компьютера. Они не заменяют проверку реального оборудования.

Для производных публичных сборок задайте свой стабильный PathMap (см. Directory.Build.props), исключите PDB, runtime Data и логи. Цифровая подпись и совместимость с политиками запуска - отдельные вопросы; отключение защиты не требуется и не предлагается.

При повторной проверке прямой запуск тестового EXE блокировался политикой компьютера. Те же 100 тестов успешно выполнены через установленный .NET командой выше, без изменения защиты. Успешная сборка интерфейса не подтверждает разрешение его запуска политикой компьютера.
