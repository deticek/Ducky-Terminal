using System.Security.Cryptography;

namespace terminal.Commands;

public static class Password
{
    private static string PasswordFile
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "config.json")))
                    return Path.Combine(directory.FullName, "password.dat");

                if (File.Exists(Path.Combine(directory.FullName, ".ducky-published")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "Config")))
                {
                    return Path.Combine(directory.FullName, "Config", "password.dat");
                }

                if (File.Exists(Path.Combine(directory.FullName, "terminal.csproj")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "Config")))
                {
                    return Path.Combine(directory.FullName, "Config", "password.dat");
                }

                directory = directory.Parent;
            }

            return Path.Combine(AppContext.BaseDirectory, "password.dat");
        }
    }

    public static bool HasPassword() => File.Exists(PasswordFile);

    public static bool CheckPassword(string password)
    {
        if (!File.Exists(PasswordFile))
            return false;

        try
        {
            string[] parts = File.ReadAllText(PasswordFile).Split(':');
            if (parts.Length != 2)
                return false;

            byte[] salt = Convert.FromHexString(parts[0]);
            byte[] storedHash = Convert.FromHexString(parts[1]);
            byte[] inputHash = HashPassword(password, salt);
            return CryptographicOperations.FixedTimeEquals(storedHash, inputHash);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static byte[] HashPassword(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);

    private static void SetPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = HashPassword(password, salt);
        Directory.CreateDirectory(Path.GetDirectoryName(PasswordFile)!);
        File.WriteAllText(PasswordFile, $"{Convert.ToHexString(salt)}:{Convert.ToHexString(hash)}");
        Console.WriteLine("Password saved.");
    }

    private static void RemovePassword()
    {
        File.Delete(PasswordFile);
        Console.WriteLine("Password removed.");
    }

    public static void PasswordHelp()
    {
        Console.WriteLine("""
            PASSWORD [action]

              /?  Show help
              /S  Set a password (only when none is set)
              /C  Change the existing password
              /R  Remove password protection
            """);
    }

    public static void PasswordManager(string argument)
    {
        string action = argument.Trim().TrimStart('/', '-').Trim().ToUpperInvariant();
        if (action is "?" or "HELP")
        {
            PasswordHelp();
            return;
        }

        if (action == "S")
        {
            if (HasPassword())
            {
                Console.WriteLine("A password is already set.");
                return;
            }

            string? password = ReadNewPassword();
            if (password is not null)
                SetPassword(password);
            return;
        }

        if (action is "C" or "R")
        {
            if (!HasPassword())
            {
                Console.WriteLine("No password is set.");
                return;
            }

            Console.Write("Enter current password: ");
            if (!CheckPassword(ReadPassword()))
            {
                Console.WriteLine("Wrong password.");
                return;
            }

            if (action == "R")
            {
                RemovePassword();
                return;
            }

            string? newPassword = ReadNewPassword();
            if (newPassword is not null)
                SetPassword(newPassword);
            return;
        }

        PasswordHelp();
    }

    private static string? ReadNewPassword()
    {
        Console.Write("Enter a password: ");
        string first = ReadPassword();
        Console.Write("Verify password: ");
        string second = ReadPassword();
        if (string.IsNullOrEmpty(first))
        {
            Console.WriteLine("Password cannot be empty.");
            return null;
        }
        if (!string.Equals(first, second, StringComparison.Ordinal))
        {
            Console.WriteLine("Passwords do not match.");
            return null;
        }
        return first;
    }

    private static string ReadPassword()
    {
        var password = new System.Text.StringBuilder();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return password.ToString();
            }
            if (key.Key == ConsoleKey.Escape)
            {
                Console.WriteLine();
                return string.Empty;
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                {
                    password.Length--;
                    Console.Write("\b \b");
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
                Console.Write('*');
            }
        }
    }
}
