# Встановлення MessagePack у Unity / MessagePack Installation for Unity

> Інструкція для Unity 2022.3.12f1 і новіших версій з MessagePack v3.x
> Guide for Unity 2022.3.12f1+ with MessagePack v3.x

---

## 🇺🇦 Українська інструкція

### Крок 1 — Встановити NuGetForUnity

NuGetForUnity дозволяє встановлювати NuGet пакети прямо в Unity без ручного копіювання DLL.

1. Відкрийте **Window → Package Manager**
2. Натисніть **+** у верхньому лівому куті
3. Оберіть **Add package from git URL**
4. Введіть:

```
https://github.com/GlitchEnzo/NuGetForUnity.git?path=/src/NuGetForUnity
```

5. Натисніть **Add** і дочекайтесь завершення імпорту

---

### Крок 2 — Встановити MessagePack core через NuGet

1. У меню Unity оберіть **NuGet → Manage NuGet Packages**
2. У рядку пошуку введіть `MessagePack`
3. Знайдіть пакет **MessagePack** від *neuecc / MessagePack-CSharp*
4. Натисніть **Install**

> ⚠️ Встановіть саме `MessagePack`, а не `MessagePack.Annotations` чи `MessagePack.Analyzer` — вони опціональні.

---

### Крок 3 — Встановити Unity-клієнт MessagePack через UPM

1. Відкрийте **Window → Package Manager**
2. Натисніть **+** → **Add package from git URL**
3. Введіть:

```
https://github.com/MessagePack-CSharp/MessagePack-CSharp.git?path=src/MessagePack.UnityClient/Assets/Scripts/MessagePack
```

4. Натисніть **Add**

---

### Крок 4 — Перевірити встановлення

Створіть тестовий скрипт і прикріпіть до будь-якого GameObject:

```csharp
using UnityEngine;
using MessagePack;

[MessagePackObject(true)]
public class TestData
{
    public int Id { get; set; }
    public string Name { get; set; }
}

public class MessagePackTest : MonoBehaviour
{
    void Start()
    {
        var data = new TestData { Id = 1, Name = "Test" };

        byte[] bytes = MessagePackSerializer.Serialize(data);
        Debug.Log($"Серіалізовано: {bytes.Length} байт");

        var restored = MessagePackSerializer.Deserialize<TestData>(bytes);
        Debug.Log($"Id: {restored.Id}, Name: {restored.Name}");
    }
}
```

Якщо в консолі бачите два рядки без помилок — MessagePack встановлено успішно.

---

### Можливі проблеми

| Помилка | Причина | Рішення |
|---|---|---|
| `IMessagePackFormatter<> could not be found` | Встановлено тільки Unity-клієнт без core | Виконайте Крок 2 (NuGet) |
| `CS0246: MessagePackSerializer not found` | Unity-клієнт не встановлено | Виконайте Крок 3 (UPM) |
| Помилки після оновлення Unity | Кеш пакетів застарів | Видаліть `Library/PackageCache` і перезапустіть Unity |
| IL2CPP build fails | Stripping видаляє типи | Додайте `link.xml` або встановіть Managed Stripping Level = Minimal |

---

### link.xml для IL2CPP (якщо потрібен)

Створіть файл `Assets/link.xml`:

```xml
<linker>
  <assembly fullname="MessagePack" preserve="all"/>
  <assembly fullname="MessagePack.Unity" preserve="all"/>
</linker>
```

---

---

## 🇬🇧 English Guide

### Step 1 — Install NuGetForUnity

NuGetForUnity allows installing NuGet packages directly into Unity without manually copying DLL files.

1. Open **Window → Package Manager**
2. Click **+** in the top left corner
3. Select **Add package from git URL**
4. Enter:

```
https://github.com/GlitchEnzo/NuGetForUnity.git?path=/src/NuGetForUnity
```

5. Click **Add** and wait for the import to finish

---

### Step 2 — Install MessagePack core via NuGet

1. In the Unity menu select **NuGet → Manage NuGet Packages**
2. Search for `MessagePack`
3. Find the **MessagePack** package by *neuecc / MessagePack-CSharp*
4. Click **Install**

> ⚠️ Install `MessagePack` itself, not `MessagePack.Annotations` or `MessagePack.Analyzer` — those are optional.

---

### Step 3 — Install MessagePack Unity client via UPM

1. Open **Window → Package Manager**
2. Click **+** → **Add package from git URL**
3. Enter:

```
https://github.com/MessagePack-CSharp/MessagePack-CSharp.git?path=src/MessagePack.UnityClient/Assets/Scripts/MessagePack
```

4. Click **Add**

---

### Step 4 — Verify the installation

Create a test script and attach it to any GameObject:

```csharp
using UnityEngine;
using MessagePack;

[MessagePackObject(true)]
public class TestData
{
    public int Id { get; set; }
    public string Name { get; set; }
}

public class MessagePackTest : MonoBehaviour
{
    void Start()
    {
        var data = new TestData { Id = 1, Name = "Test" };

        byte[] bytes = MessagePackSerializer.Serialize(data);
        Debug.Log($"Serialized: {bytes.Length} bytes");

        var restored = MessagePackSerializer.Deserialize<TestData>(bytes);
        Debug.Log($"Id: {restored.Id}, Name: {restored.Name}");
    }
}
```

If you see two log lines in the Console without errors — MessagePack is installed successfully.

---

### Troubleshooting

| Error | Cause | Fix |
|---|---|---|
| `IMessagePackFormatter<> could not be found` | Only Unity client installed, missing core | Complete Step 2 (NuGet) |
| `CS0246: MessagePackSerializer not found` | Unity client not installed | Complete Step 3 (UPM) |
| Errors after Unity update | Package cache is stale | Delete `Library/PackageCache` and restart Unity |
| IL2CPP build fails | Stripping removes required types | Add `link.xml` or set Managed Stripping Level = Minimal |

---

### link.xml for IL2CPP (if needed)

Create a file at `Assets/link.xml`:

```xml
<linker>
  <assembly fullname="MessagePack" preserve="all"/>
  <assembly fullname="MessagePack.Unity" preserve="all"/>
</linker>
```
