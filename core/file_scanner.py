"""
core/file_scanner.py
Escáner de archivos para CC Suite Pro
"""

import os
import hashlib
import time
from pathlib import Path
from datetime import datetime
from typing import Dict, List, Optional, Generator, Callable
import logging
import json

logger = logging.getLogger(__name__)


class FileScanner:
    """Escáner de archivos de contenido personalizado"""

    # Extensiones soportadas
    SUPPORTED_EXTENSIONS = {
        ".package": "CC/Mod Package",
        ".ts4script": "Script Mod",
        ".blueprint": "Blueprint",
        ".bpi": "Blueprint Image",
        ".householdbinary": "Household",
        ".sgi": "Save Game Image",
        ".hhi": "Household Image",
        ".rmi": "Room Image",
        ".trayitem": "Tray Item",
    }

    # Categorías por palabras clave en nombre
    CATEGORY_KEYWORDS = {
        "Cabello": ["hair", "hairstyle", "pelo", "cabello"],
        "Ropa": ["top", "bottom", "dress", "outfit", "ropa", "vestido"],
        "Zapatos": ["shoes", "sneakers", "heels", "boots", "zapatos"],
        "Accesorios": ["accessory", "necklace", "earring", "bracelet", "glasses"],
        "Maquillaje": ["makeup", "lipstick", "eyeshadow", "eyeliner", "blush"],
        "Piel": ["skin", "overlay", "default", "piel"],
        "Muebles": ["furniture", "chair", "table", "bed", "sofa", "mueble"],
        "Decoración": ["decor", "clutter", "painting", "rug", "plant", "decoracion"],
        "Construcción": ["build", "wall", "floor", "door", "window", "construccion"],
        "Gameplay": ["mod", "script", "gameplay", "fix", "tuning"],
        "Rasgos": ["trait", "aspiration", "career", "rasgo"],
        "Poses": ["pose", "animation", "poses"],
        "Recolors": ["recolor", "recolour"],
    }

    def __init__(self, db_manager=None):
        """
        Inicializa el escáner

        Args:
            db_manager: Instancia de DatabaseManager (opcional)
        """
        self.db = db_manager
        self.scan_results = {
            "total_files": 0,
            "new_files": 0,
            "updated_files": 0,
            "skipped_files": 0,
            "error_files": 0,
            "total_size": 0,
            "duration": 0,
            "files": [],
        }

    # ============================================================
    # ESCANEO PRINCIPAL
    # ============================================================

    def scan_directory(
        self,
        directory: str,
        recursive: bool = True,
        file_types: List[str] = None,
        progress_callback: Optional[Callable[[int], None]] = None,
    ) -> Dict:
        """
        Escanea un directorio en busca de archivos CC

        Args:
            directory: Ruta del directorio a escanear
            recursive: Si debe escanear subcarpetas
            file_types: Lista de extensiones a buscar (por defecto: .package y .ts4script)
            progress_callback: Función opcional que recibe un entero 0-100
                indicando el porcentaje de avance del escaneo. Se llama una
                vez al inicio (0) y una vez por cada archivo procesado.

        Returns:
            Diccionario con resultados del escaneo
        """
        if not os.path.exists(directory):
            logger.error(f"Directorio no encontrado: {directory}")
            return {"error": f"Directorio no encontrado: {directory}"}

        if file_types is None:
            file_types = [".package", ".ts4script"]

        # Reiniciar resultados
        self.scan_results = {
            "total_files": 0,
            "new_files": 0,
            "updated_files": 0,
            "skipped_files": 0,
            "error_files": 0,
            "total_size": 0,
            "duration": 0,
            "files": [],
            "directory": directory,
            "scan_date": datetime.now().isoformat(),
        }

        start_time = time.time()
        logger.info(f"Iniciando escaneo en: {directory}")
        print(f"\n🔍 Escaneando: {directory}")
        print("═" * 50)

        try:
            # Registrar escaneo en BD si está disponible
            scan_id = None
            if self.db:
                scan_id = self.db.start_scan(directory)

            # Recolectar archivos primero. Esto nos da el total exacto
            # de antemano, necesario para calcular un porcentaje de
            # avance real en vez de solo un modo indeterminado.
            files_to_scan = list(self._get_files(directory, recursive, file_types))
            total = len(files_to_scan)

            if progress_callback:
                progress_callback(0)

            # Escanear archivos
            for index, file_path in enumerate(files_to_scan, start=1):
                try:
                    file_info = self._analyze_file(file_path)

                    if file_info:
                        self.scan_results["files"].append(file_info)
                        self.scan_results["total_files"] += 1
                        self.scan_results["total_size"] += file_info["size"]

                        # Verificar si es nuevo o actualizado
                        if self.db:
                            existing = self.db.get_package_by_path(
                                file_info["filepath"]
                            )
                            if existing:
                                if existing["size"] != file_info["size"]:
                                    self.scan_results["updated_files"] += 1
                                    print(f"  📝 Actualizado: {file_info['filename']}")
                                else:
                                    self.scan_results["skipped_files"] += 1
                            else:
                                self.scan_results["new_files"] += 1
                                print(f"  ✨ Nuevo: {file_info['filename']}")

                            # Guardar en BD
                            self.db.insert_package(file_info)
                        else:
                            print(f"  📄 Encontrado: {file_info['filename']}")

                except Exception as e:
                    logger.error(f"Error procesando {file_path}: {e}")
                    self.scan_results["error_files"] += 1
                    print(f"  ❌ Error: {os.path.basename(file_path)}")

                # Reportar avance sin importar si el archivo individual
                # falló o no: lo que le interesa a la UI es cuántos de
                # los "total" ya se procesaron, para que la barra de
                # progreso siempre termine en 100%.
                if progress_callback:
                    percent = int((index / total) * 100) if total else 100
                    progress_callback(percent)

            # Calcular duración
            self.scan_results["duration"] = round(time.time() - start_time, 2)

            # Completar escaneo en BD
            if self.db and scan_id:
                self.db.complete_scan(scan_id, self.scan_results)

            # Mostrar resumen
            self._print_summary()

            logger.info(
                f"Escaneo completado: {self.scan_results['total_files']} archivos"
            )
            return self.scan_results

        except Exception as e:
            logger.error(f"Error durante el escaneo: {e}")
            return {"error": str(e)}

    # ============================================================
    # ANÁLISIS DE ARCHIVOS
    # ============================================================

    def _analyze_file(self, filepath: str) -> Optional[Dict]:
        """
        Analiza un archivo individual

        Args:
            filepath: Ruta completa del archivo

        Returns:
            Diccionario con información del archivo o None si error
        """
        try:
            path = Path(filepath)

            # Información básica del archivo
            stat = path.stat()

            # Calcular hashes
            md5_hash = self._calculate_hash(filepath, "md5")

            # Detectar categoría
            category = self._detect_category(path.name)

            # Detectar posible autor (por nombre de carpeta)
            author = self._detect_author(path)

            # Construir información
            file_info = {
                "filename": path.name,
                "filepath": str(path.absolute()),
                "size": stat.st_size,
                "extension": path.suffix.lower(),
                "md5_hash": md5_hash,
                "sha256_hash": None,  # Opcional, más lento
                "author": author,
                "category": category,
                "tags": self._generate_tags(path.name, category),
                "created_date": datetime.fromtimestamp(stat.st_ctime).isoformat(),
                "modified_date": datetime.fromtimestamp(stat.st_mtime).isoformat(),
                "metadata": {
                    "parent_folder": path.parent.name,
                    "depth": len(path.relative_to(path.anchor).parts),
                },
            }

            return file_info

        except Exception as e:
            logger.error(f"Error analizando {filepath}: {e}")
            return None

    def _get_files(
        self, directory: str, recursive: bool, extensions: List[str]
    ) -> Generator[str, None, None]:
        """
        Generador de archivos a escanear

        Args:
            directory: Directorio raíz
            recursive: Si debe incluir subcarpetas
            extensions: Extensiones permitidas

        Yields:
            Ruta completa del archivo
        """
        if recursive:
            for root, dirs, files in os.walk(directory):
                # Ignorar carpetas ocultas
                dirs[:] = [d for d in dirs if not d.startswith(".")]

                for file in files:
                    if any(file.lower().endswith(ext) for ext in extensions):
                        yield os.path.join(root, file)
        else:
            for file in os.listdir(directory):
                filepath = os.path.join(directory, file)
                if os.path.isfile(filepath):
                    if any(file.lower().endswith(ext) for ext in extensions):
                        yield filepath

    # ============================================================
    # HERRAMIENTAS DE ANÁLISIS
    # ============================================================

    def _calculate_hash(
        self, filepath: str, algorithm: str = "md5", chunk_size: int = 8192
    ) -> str:
        """
        Calcula hash de un archivo

        Args:
            filepath: Ruta del archivo
            algorithm: 'md5' o 'sha256'
            chunk_size: Tamaño del buffer de lectura

        Returns:
            Hash hexadecimal
        """
        if algorithm == "md5":
            hasher = hashlib.md5()
        elif algorithm == "sha256":
            hasher = hashlib.sha256()
        else:
            raise ValueError(f"Algoritmo no soportado: {algorithm}")

        try:
            with open(filepath, "rb") as f:
                while True:
                    chunk = f.read(chunk_size)
                    if not chunk:
                        break
                    hasher.update(chunk)
            return hasher.hexdigest()
        except Exception as e:
            logger.error(f"Error calculando hash: {e}")
            return None

    def _detect_category(self, filename: str) -> str:
        """
        Detecta categoría basada en palabras clave del nombre

        Args:
            filename: Nombre del archivo

        Returns:
            Categoría detectada
        """
        filename_lower = filename.lower()

        for category, keywords in self.CATEGORY_KEYWORDS.items():
            for keyword in keywords:
                if keyword in filename_lower:
                    return category

        # Si no se detecta, intentar por extensión
        if filename_lower.endswith(".ts4script"):
            return "Script Mod"

        return "Sin categoría"

    def _detect_author(self, path: Path) -> str:
        """
        Intenta detectar el autor por el nombre de la carpeta contenedora

        Args:
            path: Objeto Path del archivo

        Returns:
            Nombre del autor o 'Desconocido'
        """
        # Patrones comunes: [Autor] Nombre, Autor_Nombre, etc.
        parent = path.parent.name

        # Ignorar carpetas genéricas
        generic_folders = ["mods", "cc", "packages", "downloads", "nuevo", "new"]
        if parent.lower() in generic_folders:
            return "Desconocido"

        # Limpiar prefijos comunes
        author = parent
        prefixes = ["[", "]", "by ", "By ", "BY "]
        for prefix in prefixes:
            author = author.replace(prefix, "")

        return author.strip() if author else "Desconocido"

    def _generate_tags(self, filename: str, category: str) -> List[str]:
        """
        Genera etiquetas automáticas

        Args:
            filename: Nombre del archivo
            category: Categoría detectada

        Returns:
            Lista de etiquetas
        """
        tags = [category.lower().replace(" ", "_")]
        filename_lower = filename.lower()

        # Detectar género
        if any(
            word in filename_lower
            for word in ["female", "femenino", "mujer", "woman", "girl"]
        ):
            tags.append("femenino")
        elif any(
            word in filename_lower
            for word in ["male", "masculino", "hombre", "man", "boy"]
        ):
            tags.append("masculino")
        else:
            tags.append("unisex")

        # Detectar edad
        if any(
            word in filename_lower for word in ["toddler", "infant", "bebe", "baby"]
        ):
            tags.append("infantil")
        elif any(word in filename_lower for word in ["child", "niño", "niña", "kid"]):
            tags.append("niño")
        elif any(word in filename_lower for word in ["teen", "adolescente"]):
            tags.append("adolescente")
        elif any(word in filename_lower for word in ["elder", "anciano", "mayor"]):
            tags.append("anciano")
        else:
            tags.append("adulto")

        # Detectar estilo
        if any(word in filename_lower for word in ["maxis", "match", "mm"]):
            tags.append("maxis_match")
        elif any(word in filename_lower for word in ["alpha", "realistic", "realista"]):
            tags.append("alpha")

        return tags

    # ============================================================
    # UTILIDADES
    # ============================================================

    def _print_summary(self):
        """Imprime resumen del escaneo en consola"""
        print("\n" + "═" * 50)
        print("📊 RESUMEN DEL ESCANEO")
        print("═" * 50)
        print(f"  📁 Archivos encontrados: {self.scan_results['total_files']}")
        print(f"  ✨ Nuevos: {self.scan_results['new_files']}")
        print(f"  📝 Actualizados: {self.scan_results['updated_files']}")
        print(f"  ⏭️  Sin cambios: {self.scan_results['skipped_files']}")
        print(f"  ❌ Errores: {self.scan_results['error_files']}")
        print(
            f"  💾 Tamaño total: {self._format_size(self.scan_results['total_size'])}"
        )
        print(f"  ⏱️  Duración: {self.scan_results['duration']} segundos")
        print("═" * 50)

    def _format_size(self, size_bytes: int) -> str:
        """Formatea bytes a formato legible"""
        for unit in ["B", "KB", "MB", "GB"]:
            if size_bytes < 1024:
                return f"{size_bytes:.1f} {unit}"
            size_bytes /= 1024
        return f"{size_bytes:.1f} TB"

    def scan_sims4_mods(self) -> Dict:
        """
        Busca automáticamente la carpeta Mods de Los Sims 4

        Returns:
            Resultados del escaneo o error
        """
        # Posibles ubicaciones de la carpeta Mods
        possible_paths = [
            os.path.expanduser("~/Documents/Electronic Arts/The Sims 4/Mods"),
            os.path.expanduser("~/Documentos/Electronic Arts/Los Sims 4/Mods"),
            os.path.expanduser("~/Documents/Electronic Arts/Los Sims 4/Mods"),
        ]

        for path in possible_paths:
            if os.path.exists(path):
                print(f"✅ Carpeta Mods encontrada en: {path}")
                return self.scan_directory(path)

        print("❌ No se encontró la carpeta Mods automáticamente")
        return {"error": "Carpeta Mods no encontrada"}

    def quick_scan(self, directory: str) -> Dict:
        """
        Escaneo rápido (solo nombres y tamaños, sin hashes)

        Args:
            directory: Directorio a escanear

        Returns:
            Lista básica de archivos
        """
        files = []
        total_size = 0

        for file_path in self._get_files(directory, True, [".package", ".ts4script"]):
            path = Path(file_path)
            size = path.stat().st_size
            files.append(
                {"filename": path.name, "size": size, "folder": path.parent.name}
            )
            total_size += size

        return {
            "total_files": len(files),
            "total_size": total_size,
            "total_size_formatted": self._format_size(total_size),
            "files": files,
        }


# ============================================================
# TEST
# ============================================================

if __name__ == "__main__":
    print("🧪 Probando FileScanner...")
    print("=" * 50)

    scanner = FileScanner()

    # Test 1: Escanear directorio actual como demo
    print("\n1️⃣ Escaneo rápido del directorio actual:")
    test_dir = os.path.dirname(os.path.abspath(__file__))
    quick_result = scanner.quick_scan(test_dir)
    print(f"   Archivos .py encontrados: {quick_result['total_files']}")

    # Test 2: Probar detección de categorías
    print("\n2️⃣ Prueba de detección de categorías:")
    test_files = [
        "Wings_OE0419_F_Hair.package",
        "creator_top_crop.package",
        "[Creator] Shoes_Heels_v2.package",
        "BetterGameplay_v1.5.ts4script",
        "Unknown_CC.package",
    ]

    for filename in test_files:
        category = scanner._detect_category(filename)
        author = "Test"
        tags = scanner._generate_tags(filename, category)
        print(f"   📄 {filename}")
        print(f"      → Categoría: {category}")
        print(f"      → Tags: {', '.join(tags)}")

    # Test 3: Probar callback de progreso simulado
    print("\n3️⃣ Prueba de progress_callback:")

    def _demo_progress(pct):
        print(f"   Progreso: {pct}%")

    scanner.quick_scan(test_dir)  # solo para no tronar si no hay carpeta Mods
    scanner.scan_directory(
        test_dir, file_types=[".py"], progress_callback=_demo_progress
    )

    print("\n✅ Pruebas completadas")
