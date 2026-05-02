#!/usr/bin/env python3
"""
Auto-update version across all SmartMacroAI project files.
Usage: python update_version.py 1.5.4
"""
import sys
import re
from pathlib import Path

def update_file(filepath, old_pattern, new_value, is_regex=True):
    """Update a file with new version string."""
    try:
        content = filepath.read_text(encoding='utf-8')
        if is_regex:
            new_content = re.sub(old_pattern, new_value, content)
        else:
            new_content = content.replace(old_pattern, new_value)
        
        if new_content != content:
            filepath.write_text(new_content, encoding='utf-8')
            print(f"✅ Updated: {filepath}")
            return True
        else:
            print(f"⏭️  No change needed: {filepath}")
            return False
    except Exception as e:
        print(f"❌ Error updating {filepath}: {e}")
        return False

def main():
    if len(sys.argv) < 2:
        print("Usage: python update_version.py <version>")
        print("Example: python update_version.py 1.5.4")
        sys.exit(1)
    
    new_version = sys.argv[1]
    if not re.match(r'^\d+\.\d+\.\d+$', new_version):
        print(f"❌ Invalid version format: {new_version}")
        print("Expected format: X.Y.Z (e.g., 1.5.4)")
        sys.exit(1)
    
    # Parse version components
    major, minor, patch = new_version.split('.')
    
    # Project root
    root = Path(__file__).parent
    
    print(f"\n🚀 Updating SmartMacroAI to v{new_version}\n")
    
    # 1. SmartMacroAI.csproj
    csproj = root / "SmartMacroAI.csproj"
    update_file(csproj, 
                r'<Version>.*?</Version>',
                f'<Version>{new_version}</Version>')
    update_file(csproj,
                r'<AssemblyVersion>.*?</AssemblyVersion>',
                f'<AssemblyVersion>{major}.{minor}.{patch}.0</AssemblyVersion>')
    update_file(csproj,
                r'<FileVersion>.*?</FileVersion>',
                f'<FileVersion>{major}.{minor}.{patch}.0</FileVersion>')
    
    # 2. AssemblyInfo.cs
    assembly_info = root / "Properties" / "AssemblyInfo.cs"
    update_file(assembly_info,
                r'\[assembly: AssemblyVersion\(.*?\)\]',
                f'[assembly: AssemblyVersion("{major}.{minor}.{patch}.0")]')
    update_file(assembly_info,
                r'\[assembly: AssemblyFileVersion\(.*?\)\]',
                f'[assembly: AssemblyFileVersion("{major}.{minor}.{patch}.0")]')
    
    # 3. MainWindow.xaml.cs - CurrentVersion constant
    main_window = root / "MainWindow.xaml.cs"
    update_file(main_window,
                r'private const string CurrentVersion\s*=\s*".*?";',
                f'private const string CurrentVersion = "v{new_version}";')
    
    # 4. Installer ISS file
    iss_file = root / "installer" / "SmartMacroAI_Setup.iss"
    update_file(iss_file,
                r'#define MyAppVersion ".*?"',
                f'#define MyAppVersion "{new_version}"')
    
    # 5. README.md - badge
    readme = root / "README.md"
    update_file(readme,
                r'\[!\[Version\]\(https://img\.shields\.io/badge/version-v.*?-blue',
                f'[![Version](https://img.shields.io/badge/version-v{new_version}-blue')
    
    # 6. README.md - download links
    update_file(readme,
                r'SmartMacroAI-v.*?-win-x64\.zip',
                f'SmartMacroAI-v{new_version}-win-x64.zip')
    update_file(readme,
                r'SmartMacroAI_Setup_v.*?\.exe',
                f'SmartMacroAI_Setup_v{new_version}.exe')
    
    print(f"\n✨ Version update complete! Now run:")
    print(f"   1. dotnet build")
    print(f"   2. dotnet publish -c Release -r win-x64 --self-contained true -o publish")
    print(f"   3. ISCC installer\\SmartMacroAI_Setup.iss")
    print(f"   4. Compress-Archive -Path publish\\* -DestinationPath SmartMacroAI_v{new_version}_win-x64.zip")
    print(f"   5. git add -A && git commit -m 'Release v{new_version}'")
    print(f"   6. git tag v{new_version}")
    print(f"   7. git push origin main --tags")

if __name__ == "__main__":
    main()
