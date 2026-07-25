using System.Text;
using FareverTranslate.Core.Configuration;

namespace FareverTranslate.Core.Security;

public sealed class SecretStore
{
    private readonly AppPaths paths;

    public SecretStore(AppPaths paths)
    {
        this.paths = paths;
    }

    public bool TryLoadApiKey(out string apiKey)
    {
        apiKey = string.Empty;
        try
        {
            if (!File.Exists(paths.CredentialFile))
            {
                return false;
            }

            apiKey = Encoding.UTF8.GetString(
                DpapiProtector.Unprotect(File.ReadAllBytes(paths.CredentialFile)));
            return !string.IsNullOrWhiteSpace(apiKey);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public void SaveApiKey(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        paths.EnsureCreated();
        AtomicFile.WriteAllBytes(
            paths.CredentialFile,
            DpapiProtector.Protect(Encoding.UTF8.GetBytes(apiKey.Trim())));
    }

    public void DeleteApiKey()
    {
        if (File.Exists(paths.CredentialFile))
        {
            File.Delete(paths.CredentialFile);
        }
    }
}

