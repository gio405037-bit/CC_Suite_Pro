"""
core/organizer.py
Organizador Supremo de archivos CC
"""

import os
import shutil
import json
from pathlib import Path
from datetime import datetime
from typing import Dict, List, Optional, Callable
import logging

logger = logging.getLogger(__name__)


class OrganizerEngine:
    """Motor de organización automática de CC"""

    # Estructura de carpetas por categoría
    CATEGORY_FOLDERS = {
        "Cabello": "01_Cabello",
        "Ropa": "02_Ropa",
        "Zapatos": "03_Zapatos",
        "Accesorios": "04_Accesorios",
        "Maquillaje": "05_Maquillaje",
        "Piel": "06_Piel_y_Detalles",
        "Muebles": "07_Muebles",
        "Decoración": "08_Decoracion",
        "Construcción": "09_Construccion",
        "Gameplay": "10_Mods_Gameplay",
        "Script Mod": "11_Scripts",
        "Rasgos": "12_Rasgos_y_Aspiraciones",
        "Poses": "13_Poses_y_Animaciones",
        "Recolors": "14_Recolors",
        "Sin categoría": "99_Sin_Categoria",
    }

    # Métodos de organización disponibles
    METHODS = {
        "category": "Por Categoría",
        "author": "Por Creador",
        "date": "Por Fecha",
        "type": "Por Tipo de Archivo",
    }

    def __init__(self, db_manager=None):
        """
        Inicializa el organizador

        Args:
            db_manager: Instancia de DatabaseManager
        """
        self.db = db_manager
        self.operation_log = []
        self.stats = {"moved": 0, "skipped": 0, "errors": 0, "backup_created": False}
        self._progress_callback = None

    # ============================================================
    # ORGANIZACIÓN PRINCIPAL
    # ============================================================

    def organize(
        self,
        source_dir: str,
        target_dir: str = None,
        method: str = "category",
        create_backup: bool = True,
        dry_run: bool = False,
        progress_callback: Optional[Callable[[int], None]] = None,
    ) -> Dict:
        """
        Organiza archivos según el método especificado

        Args:
            source_dir: Directorio origen (carpeta Mods)
            target_dir: Directorio destino (si es None, usa source_dir)
            method: Método de organización ('category', 'author', 'date', 'type')
            create_backup: Si debe crear backup antes de mover
            dry_run: Si True, solo simula sin mover archivos
            progress_callback: Función opcional que recibe un entero 0-100
                con el porcentaje de avance. Se llama una vez durante el
                backup (si aplica) y luego una vez por cada archivo movido.

        Returns:
            Diccionario con resultados de la operación
        """
        if not os.path.exists(source_dir):
            return {"error": f"Directorio no encontrado: {source_dir}"}

        if target_dir is None:
            target_dir = source_dir

        # Reiniciar estadísticas
        self.stats = {
            "moved": 0,
            "skipped": 0,
            "errors": 0,
            "backup_created": False,
            "total_files": 0,
            "method": method,
            "source": source_dir,
            "target": target_dir,
            "timestamp": datetime.now().isoformat(),
        }

        logger.info(f"Iniciando organización: {method}")
        logger.info(f"Origen: {source_dir}")
        logger.info(f"Destino: {target_dir}")

        print(f"\n{'='*60}")
        print(f"  🗂️  ORGANIZADOR CC SUITE PRO")
        print(f"{'='*60}")
        print(f"  Método: {self.METHODS.get(method, method)}")
        print(f"  Modo: {'🔍 Simulación' if dry_run else '📦 Real'}")
        print(f"{'='*60}\n")

        # Guardamos el callback en la instancia para que los métodos
        # privados _organize_by_* (que no reciben este parámetro en su
        # firma) puedan reportar avance sin tener que pasarlo por todas
        # las capas de llamadas.
        self._progress_callback = progress_callback
        if progress_callback:
            progress_callback(0)

        try:
            # Crear backup si se solicita. Puede tardar bastante con
            # carpetas grandes, así que lo marcamos como un tramo fijo
            # (0-10%) del progreso total en vez de dejarlo "invisible".
            if create_backup and not dry_run:
                self._create_backup(source_dir)
                if progress_callback:
                    progress_callback(10)

            # Aplicar método de organización
            if method == "category":
                result = self._organize_by_category(source_dir, target_dir, dry_run)
            elif method == "author":
                result = self._organize_by_author(source_dir, target_dir, dry_run)
            elif method == "date":
                result = self._organize_by_date(source_dir, target_dir, dry_run)
            elif method == "type":
                result = self._organize_by_type(source_dir, target_dir, dry_run)
            else:
                return {"error": f"Método no soportado: {method}"}

            if progress_callback:
                progress_callback(100)

            # Mostrar resumen
            self._print_summary()

            return result

        except Exception as e:
            logger.error(f"Error organizando: {e}")
            return {"error": str(e)}
        finally:
            self._progress_callback = None

    def _report_progress(self, current: int, total: int):
        """
        Reporta avance dentro del tramo 10-100% (dejando 0-10% para el
        backup, si lo hubo). Si no hay callback configurado, no hace nada.
        """
        callback = self._progress_callback
        if not callback or total <= 0:
            return
        # Mapeamos 0-100% del trabajo de movido de archivos al rango
        # 10-100% visible, para que el backup y el movido de archivos
        # compartan una sola barra continua.
        percent = 10 + int((current / total) * 90)
        callback(min(percent, 100))

    # ============================================================
    # MÉTODOS DE ORGANIZACIÓN
    # ============================================================

    def _organize_by_category(self, source: str, target: str, dry_run: bool) -> Dict:
        """Organiza archivos por categoría"""
        print("📁 Creando estructura de categorías...")

        # Crear carpetas de categoría
        for category, folder_name in self.CATEGORY_FOLDERS.items():
            category_path = os.path.join(target, folder_name)
            if not dry_run:
                os.makedirs(category_path, exist_ok=True)
            print(f"  📁 {folder_name}")

        print(f"\n🔄 Moviendo archivos...")

        # Recolectamos primero para conocer el total y poder reportar
        # un porcentaje de avance real durante el movido.
        files = self._get_cc_files(source)
        total = len(files)

        # Procesar archivos
        for index, file in enumerate(files, start=1):
            # Detectar categoría por nombre
            category = self._detect_category(file.name)
            folder_name = self.CATEGORY_FOLDERS.get(category, "99_Sin_Categoria")

            # Destino
            dest_dir = os.path.join(target, folder_name)
            dest_path = os.path.join(dest_dir, file.name)

            # Evitar sobrescribir
            if os.path.exists(dest_path) and not dry_run:
                dest_path = self._get_unique_path(dest_path)

            # Mover archivo
            success = self._move_file(str(file), dest_path, dry_run)

            if success:
                self.stats["moved"] += 1
                print(f"  ✅ {file.name[:50]} → {folder_name}/")
            else:
                self.stats["skipped"] += 1

            self.stats["total_files"] += 1
            self._report_progress(index, total)

        return self.stats

    def _organize_by_author(self, source: str, target: str, dry_run: bool) -> Dict:
        """Organiza archivos por creador"""
        print("👤 Organizando por creador...")

        authors = {}

        # Primero identificar todos los autores
        for file in self._get_cc_files(source):
            author = self._detect_author(file)
            if author not in authors:
                authors[author] = []
            authors[author].append(file)

        total = sum(len(files) for files in authors.values())
        processed = 0

        # Crear carpetas y mover
        for author, files in authors.items():
            # Sanitizar nombre de carpeta
            safe_author = self._sanitize_folder_name(author)
            author_dir = os.path.join(target, safe_author)

            if not dry_run:
                os.makedirs(author_dir, exist_ok=True)

            print(f"\n  👤 {author} ({len(files)} archivos)")

            for file in files:
                dest_path = os.path.join(author_dir, file.name)

                if os.path.exists(dest_path) and not dry_run:
                    dest_path = self._get_unique_path(dest_path)

                success = self._move_file(str(file), dest_path, dry_run)

                if success:
                    self.stats["moved"] += 1
                else:
                    self.stats["skipped"] += 1

                self.stats["total_files"] += 1
                processed += 1
                self._report_progress(processed, total)

        return self.stats

    def _organize_by_date(self, source: str, target: str, dry_run: bool) -> Dict:
        """Organiza archivos por fecha de modificación"""
        print("📅 Organizando por fecha...")

        files = self._get_cc_files(source)
        total = len(files)

        for index, file in enumerate(files, start=1):
            # Obtener fecha
            mtime = os.path.getmtime(str(file))
            date = datetime.fromtimestamp(mtime)

            # Crear carpeta Año/Mes
            folder_name = date.strftime("%Y/%m_%B")
            dest_dir = os.path.join(target, folder_name)

            if not dry_run:
                os.makedirs(dest_dir, exist_ok=True)

            dest_path = os.path.join(dest_dir, file.name)

            if os.path.exists(dest_path) and not dry_run:
                dest_path = self._get_unique_path(dest_path)

            success = self._move_file(str(file), dest_path, dry_run)

            if success:
                self.stats["moved"] += 1
                print(f"  ✅ {file.name[:50]} → {folder_name}/")

            self.stats["total_files"] += 1
            self._report_progress(index, total)

        return self.stats

    def _organize_by_type(self, source: str, target: str, dry_run: bool) -> Dict:
        """Organiza por tipo de archivo (.package, .ts4script, etc.)"""
        print("📦 Organizando por tipo de archivo...")

        type_folders = {
            ".package": "CC_Packages",
            ".ts4script": "Scripts",
            ".blueprint": "Blueprints",
            ".bpi": "Imagenes_Blueprint",
        }

        files = self._get_cc_files(source)
        total = len(files)

        for index, file in enumerate(files, start=1):
            ext = file.suffix.lower()
            folder = type_folders.get(ext, "Otros")

            dest_dir = os.path.join(target, folder)

            if not dry_run:
                os.makedirs(dest_dir, exist_ok=True)

            dest_path = os.path.join(dest_dir, file.name)
            success = self._move_file(str(file), dest_path, dry_run)

            if success:
                self.stats["moved"] += 1

            self.stats["total_files"] += 1
            self._report_progress(index, total)

        return self.stats

    # ============================================================
    # HERRAMIENTAS DE ORGANIZACIÓN
    # ============================================================

    def preview_organization(self, source_dir: str, method: str = "category") -> Dict:
        """
        Vista previa de cómo quedarían organizados los archivos

        Returns:
            Diccionario con estructura simulada
        """
        preview = {}

        for file in self._get_cc_files(source_dir):
            if method == "category":
                category = self._detect_category(file.name)
                folder = self.CATEGORY_FOLDERS.get(category, "99_Sin_Categoria")
            elif method == "author":
                folder = self._detect_author(file)
            else:
                folder = "otros"

            if folder not in preview:
                preview[folder] = []

            preview[folder].append(
                {"name": file.name, "size": os.path.getsize(str(file))}
            )

        return preview

    def undo_last_operation(self) -> bool:
        """Deshace la última operación de organización"""
        if not self.operation_log:
            print("❌ No hay operaciones para deshacer")
            return False

        print("↩️ Deshaciendo última operación...")
        success_count = 0

        for operation in reversed(self.operation_log):
            source = operation.get("source")
            dest = operation.get("dest")

            if os.path.exists(dest):
                try:
                    shutil.move(dest, source)
                    print(f"  ✅ Restaurado: {os.path.basename(dest)}")
                    success_count += 1
                except Exception as e:
                    logger.error(f"Error deshaciendo: {e}")

        self.operation_log = []
        print(f"✅ {success_count} archivos restaurados")
        return True

    # ============================================================
    # UTILIDADES
    # ============================================================

    def _get_cc_files(self, directory: str) -> List[Path]:
        """Obtiene lista de archivos CC en un directorio"""
        extensions = {".package", ".ts4script", ".blueprint", ".bpi"}
        files = []

        for ext in extensions:
            files.extend(Path(directory).rglob(f"*{ext}"))

        return files

    def _detect_category(self, filename: str) -> str:
        """Detecta categoría por nombre de archivo"""
        filename_lower = filename.lower()

        keywords = {
            "Cabello": ["hair", "hairstyle", "pelo", "cabello"],
            "Ropa": [
                "top",
                "bottom",
                "dress",
                "outfit",
                "ropa",
                "vestido",
                "shirt",
                "pant",
            ],
            "Zapatos": ["shoes", "sneakers", "heels", "boots", "zapatos"],
            "Accesorios": [
                "accessory",
                "necklace",
                "earring",
                "bracelet",
                "glasses",
                "accesorio",
            ],
            "Maquillaje": [
                "makeup",
                "lipstick",
                "eyeshadow",
                "eyeliner",
                "blush",
                "maquillaje",
            ],
            "Piel": ["skin", "overlay", "default", "piel"],
            "Muebles": ["furniture", "chair", "table", "bed", "sofa", "mueble"],
            "Decoración": [
                "decor",
                "clutter",
                "painting",
                "rug",
                "plant",
                "decoracion",
            ],
            "Construcción": [
                "build",
                "wall",
                "floor",
                "door",
                "window",
                "construccion",
            ],
            "Gameplay": ["mod", "script", "gameplay", "fix", "tuning"],
            "Poses": ["pose", "animation", "poses"],
            "Recolors": ["recolor", "recolour"],
        }

        for category, words in keywords.items():
            if any(word in filename_lower for word in words):
                return category

        return "Sin categoría"

    def _detect_author(self, file_path: Path) -> str:
        """Detecta autor por nombre de carpeta padre"""
        parent = file_path.parent.name

        # Ignorar nombres genéricos
        generic = ["mods", "cc", "packages", "downloads", "nuevo", "new"]
        if parent.lower() in generic:
            return "Desconocido"

        # Limpiar formato [Autor] o Autor_Nombre
        author = parent.replace("[", "").replace("]", "").replace("_", " ")
        return author.strip() or "Desconocido"

    def _sanitize_folder_name(self, name: str) -> str:
        """Limpia un nombre para usarlo como carpeta"""
        # Reemplazar caracteres no permitidos
        invalid = '<>:"/\\|?*'
        for char in invalid:
            name = name.replace(char, "-")

        # Limitar longitud
        return name[:100].strip()

    def _get_unique_path(self, filepath: str) -> str:
        """Genera una ruta única si el archivo ya existe"""
        base, ext = os.path.splitext(filepath)
        counter = 1

        while os.path.exists(filepath):
            filepath = f"{base}_{counter}{ext}"
            counter += 1

        return filepath

    def _move_file(self, source: str, dest: str, dry_run: bool) -> bool:
        """Mueve un archivo con registro"""
        try:
            if not dry_run:
                shutil.move(source, dest)

            # Registrar operación
            self.operation_log.append(
                {
                    "source": source,
                    "dest": dest,
                    "timestamp": datetime.now().isoformat(),
                }
            )

            return True

        except Exception as e:
            logger.error(f"Error moviendo {source}: {e}")
            self.stats["errors"] += 1
            return False

    def _create_backup(self, source_dir: str):
        """Crea un backup antes de organizar"""
        backup_dir = os.path.join(
            os.path.dirname(source_dir),
            f"CC_Backup_{datetime.now().strftime('%Y%m%d_%H%M%S')}",
        )

        print(f"💾 Creando backup en: {backup_dir}")

        try:
            shutil.copytree(source_dir, backup_dir)
            self.stats["backup_created"] = True
            self.stats["backup_path"] = backup_dir
            print("  ✅ Backup creado exitosamente")
        except Exception as e:
            logger.error(f"Error creando backup: {e}")
            print(f"  ⚠️ No se pudo crear backup: {e}")

    def _print_summary(self):
        """Imprime resumen de la operación"""
        print(f"\n{'='*60}")
        print(f"  📊 RESUMEN")
        print(f"{'='*60}")
        print(f"  📁 Total archivos: {self.stats['total_files']}")
        print(f"  ✅ Movidos: {self.stats['moved']}")
        print(f"  ⏭️  Omitidos: {self.stats['skipped']}")
        print(f"  ❌ Errores: {self.stats['errors']}")

        if self.stats.get("backup_created"):
            print(f"  💾 Backup: {self.stats.get('backup_path', 'N/A')}")

        print(f"{'='*60}\n")


# ============================================================
# INTEGRACIÓN CON EL DASHBOARD
# ============================================================


class OrganizerWidget:
    """Widget para integrar el organizador en el dashboard"""

    def __init__(self, db_manager=None):
        self.engine = OrganizerEngine(db_manager)

    def organize_by_category(self, source_dir: str, dry_run: bool = False):
        """Organizar por categoría"""
        return self.engine.organize(source_dir, method="category", dry_run=dry_run)

    def organize_by_author(self, source_dir: str, dry_run: bool = False):
        """Organizar por autor"""
        return self.engine.organize(source_dir, method="author", dry_run=dry_run)

    def preview(self, source_dir: str):
        """Vista previa de organización"""
        return self.engine.preview_organization(source_dir)

    def undo(self):
        """Deshacer última operación"""
        return self.engine.undo_last_operation()


# ============================================================
# TEST
# ============================================================

if __name__ == "__main__":
    print("🧪 Probando Organizador...")
    print("=" * 60)

    organizer = OrganizerEngine()

    # Test 1: Detección de categorías
    print("\n1️⃣ Prueba de detección:")
    test_files = [
        "Wings_OE0419_F_Hair.package",
        "creator_top_crop.package",
        "[Creator] Shoes_Heels_v2.package",
        "BetterGameplay_v1.5.ts4script",
        "Unknown_CC.package",
    ]

    for f in test_files:
        category = organizer._detect_category(f)
        print(f"   {f[:40]:<40} → {category}")

    # Test 2: Preview de organización
    print("\n2️⃣ Preview de organización (directorio actual):")
    current_dir = os.path.dirname(os.path.abspath(__file__))
    preview = organizer.preview_organization(current_dir)

    for folder, files in preview.items():
        print(f"   📁 {folder}: {len(files)} archivos")

    print("\n✅ Pruebas completadas")
    print("\n💡 Para usar en el dashboard, agrega esto a main.py:")
    print("   from core.organizer import OrganizerEngine")
