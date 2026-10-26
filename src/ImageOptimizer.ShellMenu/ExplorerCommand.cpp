// "Optimize with Image Optimizer" in File Explorer's right-click menu, for the MSIX package.
//
// A packaged app can't add the classic registry verbs, so Package.appxmanifest registers this COM class for
// each supported extension and for folders instead. Explorer loads it in a surrogate process, and on
// Windows 11 it shows in the compact menu rather than under "Show more options".
// Choosing it starts ImageOptimizer.exe (next to this DLL) with the selected paths; SingleInstance gathers
// them into one window.

#include <windows.h>
#include <shlwapi.h>
#include <shobjidl_core.h>
#include <wrl/client.h>
#include <wrl/implements.h>
#include <wrl/module.h>

#include <string>
#include <string_view>
#include <vector>

#include "Labels.g.h"

using Microsoft::WRL::ClassicCom;
using Microsoft::WRL::ComPtr;
using Microsoft::WRL::InProc;
using Microsoft::WRL::Module;
using Microsoft::WRL::RuntimeClass;
using Microsoft::WRL::RuntimeClassFlags;

extern "C" IMAGE_DOS_HEADER __ImageBase;

namespace
{
  // Keep in step with ExplorerContextMenu in the app, which turns the entry on and off from Settings.
  constexpr wchar_t SettingsKey[] = L"Software\\ImageOptimizer";
  constexpr wchar_t EnabledValue[] = L"ExplorerMenu";

  // Windows allows 32,767 characters; leave room for the exe's path.
  constexpr size_t MaxCommandLine = 30000;

  std::wstring AppPath()
  {
    std::wstring path(MAX_PATH, L'\0');
    for (;;)
    {
      auto length =
          GetModuleFileNameW(reinterpret_cast<HMODULE>(&__ImageBase), path.data(), static_cast<DWORD>(path.size()));
      if (length == 0)
        return {};
      if (length < path.size())
      {
        path.resize(length);
        break;
      }
      path.resize(path.size() * 2);
    }
    path.resize(path.find_last_of(L'\\') + 1);
    return path + L"ImageOptimizer.exe";
  }

  /// The label in the Windows display language, falling back like .NET resources do (ja-JP, then ja, then English).
  std::wstring_view Label()
  {
    ULONG count = 0;
    ULONG size = 0;
    std::wstring languages;
    if (GetUserPreferredUILanguages(MUI_LANGUAGE_NAME, &count, nullptr, &size) && size > 0)
    {
      languages.resize(size);
      if (!GetUserPreferredUILanguages(MUI_LANGUAGE_NAME, &count, languages.data(), &size))
        languages.clear();
    }

    std::wstring language = languages.c_str(); // The first in the list.
    while (!language.empty())
    {
      for (const auto& label : MenuLabels)
      {
        if (*label.Language && CompareStringOrdinal(label.Language, -1, language.c_str(), -1, TRUE) == CSTR_EQUAL)
          return label.Text;
      }
      auto dash = language.find_last_of(L'-');
      language.resize(dash == std::wstring::npos ? 0 : dash);
    }
    for (const auto& label : MenuLabels)
    {
      if (!*label.Language)
        return label.Text;
    }
    return L"Optimize with Image Optimizer";
  }

  /// On unless it's been turned off in the app's Settings.
  bool IsEnabled()
  {
    DWORD value = 1;
    DWORD size = sizeof(value);
    if (RegGetValueW(HKEY_CURRENT_USER, SettingsKey, EnabledValue, RRF_RT_REG_DWORD, nullptr, &value, &size) !=
        ERROR_SUCCESS)
      return true;
    return value != 0;
  }

  /// Quotes a path for CommandLineToArgvW, where backslashes before the closing quote would escape it ("D:\").
  void AppendArgument(std::wstring& commandLine, std::wstring_view path)
  {
    commandLine += L" \"";
    commandLine += path;
    if (!path.empty() && path.back() == L'\\')
      commandLine += L'\\';
    commandLine += L'"';
  }

  bool Start(const std::wstring& app, std::wstring commandLine)
  {
    STARTUPINFOW startup{sizeof(startup)};
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(app.c_str(), commandLine.data(), nullptr, nullptr, FALSE, 0, nullptr, nullptr, &startup,
                        &process))
      return false;
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    return true;
  }
} // namespace

class __declspec(uuid("4D4A160B-D5FA-4133-BFB2-449803C652EF")) OptimizeCommand final
    : public RuntimeClass<RuntimeClassFlags<ClassicCom>, IExplorerCommand>
{
public:
  IFACEMETHODIMP GetTitle(IShellItemArray*, LPWSTR* name) override
  {
    return SHStrDupW(std::wstring(Label()).c_str(), name);
  }

  IFACEMETHODIMP GetIcon(IShellItemArray*, LPWSTR* icon) override
  {
    auto app = AppPath();
    if (app.empty())
    {
      *icon = nullptr;
      return E_FAIL;
    }
    return SHStrDupW((app + L",0").c_str(), icon);
  }

  IFACEMETHODIMP GetToolTip(IShellItemArray*, LPWSTR* tip) override
  {
    *tip = nullptr;
    return E_NOTIMPL;
  }

  IFACEMETHODIMP GetCanonicalName(GUID* name) override
  {
    *name = __uuidof(OptimizeCommand);
    return S_OK;
  }

  IFACEMETHODIMP GetState(IShellItemArray*, BOOL, EXPCMDSTATE* state) override
  {
    *state = IsEnabled() ? ECS_ENABLED : ECS_HIDDEN;
    return S_OK;
  }

  IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override
  {
    if (!items)
      return E_INVALIDARG;
    DWORD count = 0;
    auto hr = items->GetCount(&count);
    if (FAILED(hr))
      return hr;

    auto app = AppPath();
    if (app.empty())
      return HRESULT_FROM_WIN32(GetLastError());
    std::wstring prefix = L"\"" + app + L"\"";

    // The app brings its window to the front, which Windows only allows if the process that started it says so.
    AllowSetForegroundWindow(ASFW_ANY);

    // Very large selections are split over several launches; the later copies hand their files to the first.
    std::wstring commandLine = prefix;
    bool started = false;
    for (DWORD i = 0; i < count; i++)
    {
      ComPtr<IShellItem> item;
      if (FAILED(items->GetItemAt(i, &item)))
        continue;
      LPWSTR path = nullptr;
      if (FAILED(item->GetDisplayName(SIGDN_FILESYSPATH, &path)))
        continue; // Not a file on disk, such as an item inside a zip file.
      std::wstring_view view(path);
      if (commandLine.size() > prefix.size() && commandLine.size() + view.size() + 4 > MaxCommandLine)
      {
        started |= Start(app, commandLine);
        commandLine = prefix;
      }
      AppendArgument(commandLine, view);
      CoTaskMemFree(path);
    }
    if (commandLine.size() > prefix.size())
      started |= Start(app, commandLine);
    return started ? S_OK : HRESULT_FROM_WIN32(GetLastError());
  }

  IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override
  {
    *flags = ECF_DEFAULT;
    return S_OK;
  }

  IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override
  {
    *commands = nullptr;
    return E_NOTIMPL;
  }
};

CoCreatableClass(OptimizeCommand)

STDAPI DllGetClassObject(REFCLSID clsid, REFIID iid, void** result)
{
  return Module<InProc>::GetModule().GetClassObject(clsid, iid, result);
}

STDAPI DllCanUnloadNow()
{
  return Module<InProc>::GetModule().Terminate() ? S_OK : S_FALSE;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, void*)
{
  if (reason == DLL_PROCESS_ATTACH)
    DisableThreadLibraryCalls(instance);
  return TRUE;
}
