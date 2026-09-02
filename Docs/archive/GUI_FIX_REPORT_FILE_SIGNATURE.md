# GUI Fix Report --file-signature

## Diagnostic Technique Confirmed
- Bug identified: underscore vs hyphen in mapping CLI options
- Localisation: `smbeagle_gui.py` metadata options and build_command logic
- Impact: option `--file_signature` not recognised by SMBeagle.exe

## Approche de Correction Choisie
- [x] Option A : Mapping Dictionary
- [ ] Option B : Modification Directe
- [ ] Option C : Transformation Dynamique
- Justification : Preserve profile keys while mapping to correct CLI flag

## Modifications Appliquées
- `smbeagle_gui.py` : added `cli_option_mapping` dict and updated checkbox labels and command builder
- `test_cli_mapping.py` : new tests verifying mapping and profile compatibility

## Tests de Validation Réalisés
- GUI launch : N/A (headless tests use stubs)
- Command building : PASS
- Profile compatibility : PASS
- Integration with SMBeagle : Build succeeded

## Régression Testing
- Confirmed other metadata options unaffected through existing profiles
- Profiles YAML remain compatible
- Interface user labels updated but behaviour unchanged

## Impact Utilisateur
- GUI now generates correct `--file-signature` option
- Existing profiles continue to function without modification
- User experience otherwise unchanged
