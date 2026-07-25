"""
core/duplicate_detector.py
Detector de archivos CC duplicados
"""

import os
import hashlib
from pathlib import Path
from datetime import datetime
from typing import Dict, List, Optional, Tuple
import logging

logger = logging.getLogger(__name__)


class DuplicateDetector:
    """Detector de archivos duplicados por hash y nombre"""

    def __init__(self, db_manager=None):
        self.db = db_manager
        self.results = {
            "total_scanned": 0,
            "duplicates_found": 0,
            "groups": [],
            "wasted_space": 0,
        }

    # ============================================================
    # DETECCIÓN POR HASH MD5 (Método exacto)
    # ============================================================

    def find_by_hash(self, directory: str) -> Dict:
        """
        Encuentra duplicados exactos por hash MD5

        Args:
            directory: Directorio a escanear

        Returns:
            Diccionario con grupos de duplicados
        """
        print(f"\n{'='*60}")
        print(f"  🔍 DETECTOR DE DUPLICADOS - Modo Hash MD5")
        print(f"{'='*60}")
        print(f"  Directorio: {directory}")
        print(f"  Método: Comparación exacta (MD5)")
        print(f"{'='*60}\n")

        hashes = {}
        duplicates = []
        total_size_wasted = 0

        files = list(Path(directory).rglob("*.package")) + list(
            Path(directory).rglob("*.ts4script")
        )

        total = len(files)
        print(f"  📁 Analizando {total} archivos...\n")

        for i, filepath in enumerate(files):
            if i % 100 == 0:
                print(f"  ⏳ Progreso: {i}/{total} ({(i/total)*100:.0f}%)")

            try:
                file_hash = self._get_file_hash(str(filepath))
                file_size = os.path.getsize(str(filepath))

                if file_hash in hashes:
                    # ¡Duplicado encontrado!
                    original = hashes[file_hash]
                    duplicates.append(
                        {
                            "original": original["path"],
                            "duplicate": str(filepath),
                            "size": file_size,
                            "hash": file_hash[:16] + "...",
                        }
                    )
                    total_size_wasted += file_size
                    print(f"  🟡 Duplicado: {filepath.name[:60]}")
                else:
                    hashes[file_hash] = {
                        "path": str(filepath),
                        "size": file_size,
                        "name": filepath.name,
                    }

            except Exception as e:
                logger.error(f"Error procesando {filepath}: {e}")

        # Agrupar duplicados
        groups = self._group_duplicates(duplicates)

        self.results = {
            "total_scanned": total,
            "duplicates_found": len(duplicates),
            "groups": groups,
            "wasted_space": total_size_wasted,
            "wasted_space_formatted": self._format_size(total_size_wasted),
        }

        self._print_results()
        return self.results

    # ============================================================
    # DETECCIÓN POR SIMILITUD (Modo avanzado)
    # ============================================================

    def find_by_name_similarity(self, directory: str, threshold: float = 0.8) -> Dict:
        """
        Encuentra archivos con nombres muy similares

        Args:
            directory: Directorio a escanear
            threshold: Umbral de similitud (0-1)

        Returns:
            Diccionario con posibles duplicados
        """
        print(f"\n{'='*60}")
        print(f"  🔍 DETECTOR DE DUPLICADOS - Modo Similitud")
        print(f"{'='*60}\n")

        from difflib import SequenceMatcher

        files = list(Path(directory).rglob("*.package")) + list(
            Path(directory).rglob("*.ts4script")
        )

        similar_groups = []
        checked = set()

        print(f"  📁 Comparando {len(files)} archivos...\n")

        for i, file1 in enumerate(files):
            if i % 200 == 0:
                print(f"  ⏳ Progreso: {i}/{len(files)}")

            if str(file1) in checked:
                continue

            group = [str(file1)]

            for file2 in files:
                if file1 == file2 or str(file2) in checked:
                    continue

                similarity = SequenceMatcher(
                    None, file1.name.lower(), file2.name.lower()
                ).ratio()

                if similarity >= threshold:
                    group.append(str(file2))
                    checked.add(str(file2))

            if len(group) > 1:
                similar_groups.append(group)
                print(f"  🟡 Grupo similar ({len(group)} archivos): {file1.name[:50]}")

            checked.add(str(file1))

        return {
            "total_scanned": len(files),
            "similar_groups": len(similar_groups),
            "groups": similar_groups,
        }

    # ============================================================
    # DETECCIÓN POR BASE DE DATOS
    # ============================================================

    def find_in_database(self) -> Dict:
        """
        Busca duplicados usando la base de datos

        Returns:
            Lista de duplicados encontrados en BD
        """
        if not self.db:
            return {"error": "Base de datos no disponible"}

        print(f"\n{'='*60}")
        print(f"  🔍 BUSCANDO DUPLICADOS EN BASE DE DATOS")
        print(f"{'='*60}\n")

        duplicates = self.db.find_duplicates()

        if duplicates:
            print(f"  🚨 {len(duplicates)} grupos de duplicados encontrados:\n")
            for dup in duplicates:
                count = dup.get("count", 0)
                files = dup.get("files", "")
                wasted = (count - 1) * 0  # Calcular con BD
                print(f"  📦 {count}x duplicados: {files[:80]}...")
        else:
            print(f"  ✅ No se encontraron duplicados")

        return duplicates

    # ============================================================
    # ELIMINACIÓN SEGURA
    # ============================================================

    def delete_duplicates(
        self,
        duplicates: List[Dict],
        keep: str = "newest",
        create_backup: bool = True,
        dry_run: bool = True,
    ) -> Dict:
        """
        Elimina archivos duplicados

        Args:
            duplicates: Lista de duplicados a eliminar
            keep: Cuál conservar ('newest', 'oldest', 'shortest_path')
            create_backup: Si crear backup antes de eliminar
            dry_run: Si True, solo simula

        Returns:
            Resultados de la eliminación
        """
        result = {"deleted": 0, "freed_space": 0, "errors": 0, "dry_run": dry_run}

        print(f"\n{'='*60}")
        print(f"  🗑️  ELIMINANDO DUPLICADOS")
        print(f"  Modo: {'🔍 Simulación' if dry_run else '⚠️ REAL'}")
        print(f"{'='*60}\n")

        for dup in duplicates:
            duplicate_path = dup.get("duplicate", "")

            if os.path.exists(duplicate_path):
                size = os.path.getsize(duplicate_path)

                if dry_run:
                    print(
                        f"  🔍 [SIMULACIÓN] Eliminaría: {os.path.basename(duplicate_path)[:60]}"
                    )
                else:
                    try:
                        os.remove(duplicate_path)
                        print(
                            f"  ✅ Eliminado: {os.path.basename(duplicate_path)[:60]}"
                        )
                    except Exception as e:
                        print(f"  ❌ Error: {e}")
                        result["errors"] += 1
                        continue

                result["deleted"] += 1
                result["freed_space"] += size

        result["freed_space_formatted"] = self._format_size(result["freed_space"])

        print(f"\n  📊 Resultado:")
        print(f"  🗑️  Archivos eliminados: {result['deleted']}")
        print(f"  💾 Espacio liberado: {result['freed_space_formatted']}")
        print(f"  ❌ Errores: {result['errors']}")

        return result

    # ============================================================
    # UTILIDADES
    # ============================================================

    def _get_file_hash(self, filepath: str, chunk_size: int = 8192) -> str:
        """Calcula hash MD5 de un archivo"""
        hasher = hashlib.md5()
        try:
            with open(filepath, "rb") as f:
                while True:
                    chunk = f.read(chunk_size)
                    if not chunk:
                        break
                    hasher.update(chunk)
            return hasher.hexdigest()
        except Exception as e:
            logger.error(f"Error hasheando {filepath}: {e}")
            return ""

    def _group_duplicates(self, duplicates: List[Dict]) -> List[Dict]:
        """Agrupa duplicados por hash"""
        groups = {}
        for dup in duplicates:
            hash_key = dup["hash"]
            if hash_key not in groups:
                groups[hash_key] = []
            groups[hash_key].append(dup)

        return [
            {
                "hash": hash_key,
                "count": len(items) + 1,
                "items": items,
                "total_wasted": sum(item["size"] for item in items),
            }
            for hash_key, items in groups.items()
        ]

    def _format_size(self, size_bytes: int) -> str:
        """Formatea bytes a formato legible"""
        for unit in ["B", "KB", "MB", "GB"]:
            if size_bytes < 1024:
                return f"{size_bytes:.1f} {unit}"
            size_bytes /= 1024
        return f"{size_bytes:.1f} TB"

    def _print_results(self):
        """Imprime resultados del escaneo"""
        r = self.results
        print(f"\n{'='*60}")
        print(f"  📊 RESULTADOS DEL ANÁLISIS")
        print(f"{'='*60}")
        print(f"  📁 Archivos escaneados: {r['total_scanned']}")
        print(f"  🚨 Duplicados encontrados: {r['duplicates_found']}")
        print(f"  📦 Grupos de duplicados: {len(r['groups'])}")
        print(f"  💾 Espacio desperdiciado: {r['wasted_space_formatted']}")
        print(f"{'='*60}\n")

        if r["groups"]:
            print("  📋 Grupos de duplicados:")
            for i, group in enumerate(r["groups"][:10], 1):
                print(
                    f"\n  Grupo #{i} ({group['count']} archivos, {self._format_size(group['total_wasted'])} desperdiciado):"
                )
                for item in group["items"][:3]:
                    print(f"    📄 {os.path.basename(item['duplicate'])[:70]}")

    def get_quick_stats(self, directory: str) -> Dict:
        """
        Estadísticas rápidas sin escanear todo

        Returns:
            Estimación de duplicados
        """
        files = list(Path(directory).rglob("*.package"))

        # Agrupar por tamaño (los duplicados suelen tener mismo tamaño)
        size_groups = {}
        for f in files:
            size = os.path.getsize(str(f))
            if size not in size_groups:
                size_groups[size] = []
            size_groups[size].append(str(f))

        potential_duplicates = {
            size: paths for size, paths in size_groups.items() if len(paths) > 1
        }

        return {
            "total_files": len(files),
            "unique_sizes": len(size_groups),
            "potential_duplicate_groups": len(potential_duplicates),
            "potential_duplicates": sum(
                len(v) - 1 for v in potential_duplicates.values()
            ),
        }


# ============================================================
# TEST
# ============================================================

if __name__ == "__main__":
    print("🧪 Probando Detector de Duplicados...\n")

    detector = DuplicateDetector()

    # Test 1: Estadísticas rápidas del directorio actual
    print("1️⃣ Estadísticas rápidas:")
    current_dir = os.path.dirname(os.path.abspath(__file__))
    stats = detector.get_quick_stats(current_dir)
    print(f"   Archivos .py: {stats['total_files']}")
    print(f"   Tamaños únicos: {stats['unique_sizes']}")
    print(f"   Posibles grupos duplicados: {stats['potential_duplicate_groups']}")

    # Test 2: Buscar duplicados reales (solo si hay Mods)
    mods_path = os.path.expanduser("~/Documents/Electronic Arts/The Sims 4/Mods")
    if os.path.exists(mods_path):
        print(f"\n2️⃣ Buscando duplicados en carpeta Mods...")
        print("   (Puede tardar con muchos archivos)")
        # detector.find_by_hash(mods_path)  # Descomentar para prueba real

    print("\n✅ Pruebas completadas")
