using System;
using System.Collections.Generic;
using System.IO;

namespace ZenithApp.Modules.T9Predictor;

public static class BuiltinWordData
{
    public static void EnsureDictionariesExist(string outputDirectory)
    {
        string ruPath = Path.Combine(outputDirectory, "ru.dict");
        string enPath = Path.Combine(outputDirectory, "en.dict");

        if (!File.Exists(ruPath))
        {
            DictionaryBinaryBuilder.BuildBinaryFile(GetRussianWords(), ruPath);
        }

        if (!File.Exists(enPath))
        {
            DictionaryBinaryBuilder.BuildBinaryFile(GetEnglishWords(), enPath);
        }
    }

    private static IEnumerable<(string Word, uint Frequency)> GetRussianWords()
    {
        // Core frequent Russian vocabulary with realistic corpus frequencies
        var words = new (string, uint)[]
        {
            ("привет", 95000), ("приветствую", 45000), ("приветствие", 20000), ("пример", 85000), ("примерно", 60000),
            ("программа", 98000), ("программирование", 55000), ("программист", 48000), ("проект", 92000), ("проектирование", 35000),
            ("работа", 120000), ("работать", 95000), ("рабочий", 80000), ("работник", 40000), ("работает", 90000),
            ("разработка", 88000), ("разработчик", 75000), ("разрешение", 50000), ("развитие", 65000), ("разговор", 45000),
            ("пользователь", 90000), ("пользовательский", 45000), ("пожалуйста", 85000), ("помощь", 70000), ("помощник", 40000),
            ("компьютер", 75000), ("компьютерный", 38000), ("компания", 85000), ("команда", 72000), ("контроллер", 35000),
            ("сообщение", 91000), ("система", 110000), ("системный", 65000), ("сервер", 78000), ("сервис", 82000),
            ("функция", 84000), ("функционал", 52000), ("файл", 99000), ("файловый", 42000), ("форма", 60000),
            ("настройка", 87000), ("настройки", 94000), ("настроить", 45000), ("начало", 58000), ("направление", 48000),
            ("внимание", 77000), ("возможность", 88000), ("возможно", 92000), ("вопрос", 85000), ("время", 115000),
            ("документ", 79000), ("документация", 52000), ("доступ", 68000), ("действие", 71000), ("данные", 105000),
            ("интерфейс", 74000), ("информация", 96000), ("инструмент", 63000), ("интернет", 89000), ("инструкция", 47000),
            ("клавиатура", 58000), ("клавиша", 52000), ("кнопка", 69000), ("код", 93000), ("клиент", 75000),
            ("окно", 82000), ("объект", 78000), ("обновление", 71000), ("ошибка", 84000), ("обработка", 65000),
            ("подсказка", 68000), ("предпросмотр", 54000), ("перемещение", 46000), ("переключение", 49000), ("просмотр", 67000),
            ("сегодня", 86000), ("сейчас", 94000), ("создать", 76000), ("сохранить", 81000), ("статус", 63000),
            ("текст", 89000), ("текстовый", 51000), ("тестирование", 56000), ("тест", 79000), ("точка", 54000),
            ("уведомление", 59000), ("управление", 81000), ("установка", 64000), ("устройство", 67000), ("успешно", 72000),
            ("человек", 102000), ("число", 61000), ("чтобы", 95000), ("экран", 73000), ("элемент", 76000),
            ("язык", 83000), ("яндекс", 55000), ("январь", 35000), ("хорошо", 89000), ("хотя", 62000)
        };
        return words;
    }

    private static IEnumerable<(string Word, uint Frequency)> GetEnglishWords()
    {
        // Core frequent English vocabulary with realistic corpus frequencies
        var words = new (string, uint)[]
        {
            ("about", 120000), ("action", 75000), ("address", 65000), ("after", 95000), ("again", 70000),
            ("application", 98000), ("account", 85000), ("access", 80000), ("active", 62000), ("always", 72000),
            ("because", 99000), ("before", 82000), ("better", 74000), ("between", 88000), ("border", 58000),
            ("button", 79000), ("buffer", 49000), ("binary", 45000), ("browser", 68000), ("build", 81000),
            ("change", 89000), ("client", 76000), ("command", 79000), ("common", 63000), ("complete", 74000),
            ("connection", 82000), ("control", 87000), ("controller", 52000), ("current", 84000), ("cursor", 51000),
            ("database", 79000), ("default", 86000), ("delete", 72000), ("device", 75000), ("dialog", 59000),
            ("directory", 66000), ("display", 77000), ("document", 83000), ("download", 78000), ("driver", 54000),
            ("element", 73000), ("enable", 76000), ("engine", 61000), ("error", 91000), ("event", 85000),
            ("example", 88000), ("execute", 58000), ("extended", 48000), ("format", 69000), ("function", 89000),
            ("general", 71000), ("global", 64000), ("handle", 68000), ("header", 62000), ("height", 59000),
            ("helper", 53000), ("history", 66000), ("hotkey", 52000), ("information", 95000), ("initialize", 60000),
            ("interface", 84000), ("keyboard", 69000), ("language", 78000), ("layout", 71000), ("library", 73000),
            ("manager", 82000), ("margin", 55000), ("message", 93000), ("method", 80000), ("monitor", 67000),
            ("network", 84000), ("number", 90000), ("object", 87000), ("online", 74000), ("option", 77000),
            ("overlay", 56000), ("package", 68000), ("password", 89000), ("perform", 63000), ("player", 71000),
            ("position", 76000), ("predict", 48000), ("preview", 67000), ("process", 88000), ("program", 92000),
            ("project", 94000), ("property", 79000), ("quick", 68000), ("record", 65000), ("register", 64000),
            ("release", 70000), ("remove", 69000), ("request", 83000), ("resource", 76000), ("result", 86000),
            ("screen", 81000), ("search", 92000), ("select", 85000), ("service", 96000), ("setting", 89000),
            ("shortcut", 58000), ("source", 82000), ("spacing", 46000), ("status", 78000), ("string", 77000),
            ("system", 112000), ("target", 75000), ("testing", 68000), ("thickness", 47000), ("timeout", 54000),
            ("update", 88000), ("version", 87000), ("viewer", 61000), ("virtual", 64000), ("visible", 66000),
            ("welcome", 65000), ("window", 97000), ("without", 79000), ("working", 83000), ("zenith", 45000)
        };
        return words;
    }
}
