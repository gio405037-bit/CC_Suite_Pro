"""
core/engine.py
Motor principal de CC Suite Pro
Coordina todos los módulos y gestiona el estado de la aplicación
"""

import os
import sys
import json
import shutil
from datetime import datetime
from pathlib import Path
from typing import Dict, List, Optional, Any
import logging

# Agregar directorio raíz al path
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

logger = logging.getLogger(__name__)


class CCSuiteEngine:
    """
    Motor principal de CC Suite Pro
    Centraliza todas las operaciones y mantiene el estado
    """

    def __init__(self):
        # Módulos (se inicializan bajo demanda)
        self._database = None
        self._scanner = None
        self._organizer = None
        self._health_analyzer = None

        # Estado de la aplicación
        self.state = {
            "initialized": False,
            "current_profile": "default",
            "sims4_path": None,
            "mods_path": None,
            "last_scan": None,
            "total_packages": 0,
            "total_size": 0,
            "is_scanning": False,
        }

        # Configuración
        self.config = self._load_config()

        # Historial de operaciones
        self.history = []

        logger.info("CCSuiteEngine creado")

    # ============================================================
    # INICIALIZACIÓN
    # ============================================================

    def initialize(self) -> bool:
        """Inicializa el motor y todos sus componentes"""
        print("\n" + "=" * 60)
        print("  🎮 CC Suite Pro - Inicializando motor...")
        print("=" * 60)

        try:
            # 1. Base de datos
            print("  💾 Inicializando base de datos...")
            db = self.database
            if not db.initialize():
                raise Exception("No se pudo inicializar la base de datos")
            print("  ✅ Base de datos lista")

            # 2. Detectar Sims 4
            print("  🎮 Buscando Los Sims 4...")
            sims4_path = self._detect_sims4()
            if sims4_path:
                self.state["sims4_path"] = sims4_path
                self.state["mods_path"] = os.path.join(sims4_path, "Mods")
                print(f"  ✅ Sims 4 encontrado: {sims4_path}")
            else:
                print("  ⚠️ Sims 4 no detectado automáticamente")

            # 3. Cargar estadísticas
            print("  📊 Cargando estadísticas...")
            stats = db.get_statistics()
            self.state["total_packages"] = stats["total_packages"]
            self.state["total_size"] = stats["total_size"]
            print(f"  ✅ {stats['total_packages']} paquetes registrados")

            self.state["initialized"] = True
            print("=" * 60)
            print("  ✅ Motor inicializado correctamente")
            print("=" * 60 + "\n")

            return True

        except Exception as e:
            logger.error(f"Error inicializando motor: {e}")
            print(f"  ❌ Error: {e}")
            return False

    # ============================================================
    # PROPIEDADES (Lazy Loading)
    # ============================================================

    @property
    def database(self):
        """Obtiene la instancia de base de datos"""
        if self._database is None:
            from core.database import DatabaseManager

            self._database = DatabaseManager()
        return self._database

    @property
    def scanner(self):
        """Obtiene la instancia del escáner"""
        if self._scanner is None:
            from core.file_scanner import FileScanner

            self._scanner = FileScanner(self.database)
        return self._scanner

    @property
    def organizer(self):
        """Obtiene la instancia del organizador"""
        if self._organizer is None:
            from core.organizer import OrganizerEngine

            self._organizer = OrganizerEngine(self.database)
        return self._organizer

    # ============================================================
    # OPERACIONES PRINCIPALES
    # ============================================================

    def scan_mods(self, directory: str = None) -> Dict:
        """
        Escanea archivos CC/Mods

        Args:
            directory: Directorio a escanear (None = carpeta Mods detectada)

        Returns:
            Resultados del escaneo
        """
        if self.state["is_scanning"]:
            return {"error": "Ya hay un escaneo en progreso"}

        # Usar carpeta detectada si no se especifica
        if directory is None:
            directory = self.state.get("mods_path")
            if not directory:
                return {"error": "No se encontró la carpeta Mods"}

        if not os.path.exists(directory):
            return {"error": f"Directorio no existe: {directory}"}

        print(f"\n🔍 Escaneando: {directory}")
        self.state["is_scanning"] = True

        try:
            result = self.scanner.scan_directory(directory, recursive=True)

            # Actualizar estado
            self.state["last_scan"] = datetime.now().isoformat()
            self.state["total_packages"] = result.get("total_files", 0)
            self.state["total_size"] = result.get("total_size", 0)
            self.state["is_scanning"] = False

            # Registrar en historial
            self._add_to_history("scan", result)

            return result

        except Exception as e:
            self.state["is_scanning"] = False
            logger.error(f"Error en escaneo: {e}")
            return {"error": str(e)}

    def organize_cc(self, directory: str = None, method: str = "category") -> Dict:
        """
        Organiza archivos CC

        Args:
            directory: Directorio a organizar
            method: Método de organización ('category', 'author', 'date')

        Returns:
            Resultados de la organización
        """
        if directory is None:
            directory = self.state.get("mods_path")

        if not directory or not os.path.exists(directory):
            return {"error": "Directorio no válido"}

        print(f"\n🗂️ Organizando por {method}: {directory}")

        try:
            result = self.organizer.organize(
                directory, method=method, create_backup=True
            )

            self._add_to_history("organize", result)
            return result

        except Exception as e:
            logger.error(f"Error organizando: {e}")
            return {"error": str(e)}

    def find_duplicates(self) -> List[Dict]:
        """Encuentra archivos duplicados"""
        return self.database.find_duplicates()

    def get_health_report(self) -> Dict:
        """Genera reporte de salud del sistema"""
        stats = self.database.get_statistics()
        duplicates = self.find_duplicates()

        score = 100

        # Penalizaciones
        if len(duplicates) > 0:
            score -= len(duplicates) * 2

        corrupted = stats.get("corrupted", 0)
        if corrupted > 0:
            score -= corrupted * 5

        score = max(0, min(100, score))

        # Determinar estado
        if score >= 90:
            status = "Excelente"
            icon = "✅"
        elif score >= 75:
            status = "Bueno"
            icon = "🙂"
        elif score >= 60:
            status = "Atención"
            icon = "⚠️"
        elif score >= 40:
            status = "Inestable"
            icon = "🔶"
        else:
            status = "Crítico"
            icon = "🚨"

        return {
            "score": score,
            "status": status,
            "icon": icon,
            "total_packages": stats["total_packages"],
            "total_size": stats["total_size"],
            "duplicates": len(duplicates),
            "corrupted": corrupted,
            "favorites": stats.get("favorites", 0),
            "last_scan": self.state.get("last_scan"),
            "recommendations": self._get_recommendations(score, duplicates, corrupted),
        }

    def search(self, query: str) -> List[Dict]:
        """Busca paquetes por nombre, autor o categoría"""
        return self.database.search_packages(query)

    def get_packages_by_category(self) -> Dict:
        """Obtiene paquetes agrupados por categoría"""
        stats = self.database.get_statistics()
        return stats.get("category_distribution", [])

    def get_top_creators(self, limit: int = 10) -> List[Dict]:
        """Obtiene los creadores más usados"""
        stats = self.database.get_statistics()
        return stats.get("top_creators", [])[:limit]

    # ============================================================
    # PERFILES
    # ============================================================

    def create_profile(self, name: str, packages: List[int]) -> bool:
        """Crea un perfil con paquetes específicos"""
        profile = {
            "name": name,
            "packages": packages,
            "created": datetime.now().isoformat(),
        }

        profiles = self.config.get("profiles", {})
        profiles[name] = profile
        self.config["profiles"] = profiles
        self._save_config()

        return True

    def load_profile(self, name: str) -> List[int]:
        """Carga los paquetes de un perfil"""
        profiles = self.config.get("profiles", {})
        profile = profiles.get(name, {})
        return profile.get("packages", [])

    # ============================================================
    # BACKUP
    # ============================================================

    def create_backup(self, source: str = None) -> str:
        """Crea un backup de la carpeta Mods"""
        if source is None:
            source = self.state.get("mods_path")

        if not source or not os.path.exists(source):
            raise ValueError("Carpeta origen no válida")

        backup_dir = os.path.join(
            os.path.dirname(source),
            f"CC_Backup_{datetime.now().strftime('%Y%m%d_%H%M%S')}",
        )

        print(f"💾 Creando backup en: {backup_dir}")
        shutil.copytree(source, backup_dir)
        print("✅ Backup creado exitosamente")

        return backup_dir

    # ============================================================
    # EXPORTACIÓN
    # ============================================================

    def export_report(self, filepath: str = None) -> str:
        """Exporta un reporte completo"""
        if filepath is None:
            filepath = f"cc_suite_report_{datetime.now().strftime('%Y%m%d')}.json"

        report = {
            "generated": datetime.now().isoformat(),
            "health": self.get_health_report(),
            "statistics": self.database.get_statistics(),
            "duplicates": self.find_duplicates(),
            "top_creators": self.get_top_creators(),
        }

        with open(filepath, "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2, ensure_ascii=False)

        print(f"📊 Reporte exportado: {filepath}")
        return filepath

    # ============================================================
    # UTILIDADES
    # ============================================================

    def _detect_sims4(self) -> Optional[str]:
        """Detecta la instalación de Los Sims 4"""
        possible_paths = [
            os.path.expanduser("~/Documents/Electronic Arts/The Sims 4"),
            os.path.expanduser("~/Documentos/Electronic Arts/Los Sims 4"),
            os.path.expanduser("~/Documents/Electronic Arts/Los Sims 4"),
            "C:/Program Files (x86)/Origin Games/The Sims 4",
            "C:/Program Files/EA Games/The Sims 4",
        ]

        for path in possible_paths:
            if os.path.exists(path):
                return path

        return None

    def _load_config(self) -> Dict:
        """Carga la configuración"""
        config_path = Path(__file__).parent.parent / "data" / "config.json"

        default_config = {
            "language": "es",
            "theme": "dark",
            "auto_scan": False,
            "profiles": {},
            "shortcuts": {},
        }

        try:
            if config_path.exists():
                with open(config_path, "r", encoding="utf-8") as f:
                    loaded = json.load(f)
                    default_config.update(loaded)
        except Exception as e:
            logger.warning(f"Error cargando config: {e}")

        return default_config

    def _save_config(self):
        """Guarda la configuración"""
        config_path = Path(__file__).parent.parent / "data" / "config.json"

        try:
            config_path.parent.mkdir(exist_ok=True)
            with open(config_path, "w", encoding="utf-8") as f:
                json.dump(self.config, f, indent=2, ensure_ascii=False)
        except Exception as e:
            logger.error(f"Error guardando config: {e}")

    def _add_to_history(self, operation: str, result: Dict):
        """Agrega una operación al historial"""
        self.history.append(
            {
                "operation": operation,
                "timestamp": datetime.now().isoformat(),
                "result": {
                    "total_files": result.get("total_files", 0),
                    "success": "error" not in result,
                },
            }
        )

        # Mantener solo últimas 50 operaciones
        if len(self.history) > 50:
            self.history = self.history[-50:]

    def _get_recommendations(
        self, score: int, duplicates: List, corrupted: int
    ) -> List[str]:
        """Genera recomendaciones basadas en el estado"""
        recommendations = []

        if score >= 90:
            recommendations.append("✅ Sistema en excelente estado")
        elif score >= 75:
            recommendations.append("💡 Considera eliminar duplicados")
        else:
            if duplicates:
                recommendations.append(
                    f"⚠️ Eliminar {len(duplicates)} archivos duplicados"
                )
            if corrupted:
                recommendations.append(f"🚨 Reparar {corrupted} archivos corruptos")
            recommendations.append("📦 Crear un backup de seguridad")

        return recommendations

    def get_system_info(self) -> Dict:
        """Obtiene información del sistema"""
        import platform

        return {
            "app_version": "3.0.0",
            "python_version": platform.python_version(),
            "os": platform.system(),
            "os_version": platform.version(),
            "sims4_path": self.state.get("sims4_path"),
            "mods_path": self.state.get("mods_path"),
            "database_size": (
                os.path.getsize(self.database.db_path)
                if os.path.exists(self.database.db_path)
                else 0
            ),
        }


# ============================================================
# TEST
# ============================================================

if __name__ == "__main__":
    print("🧪 Probando CC Suite Engine...\n")

    engine = CCSuiteEngine()

    # Inicializar
    if engine.initialize():
        print("\n📊 Información del sistema:")
        info = engine.get_system_info()
        for key, value in info.items():
            print(f"  {key}: {value}")

        print("\n🏥 Reporte de salud:")
        health = engine.get_health_report()
        print(f"  Score: {health['score']}/100 - {health['icon']} {health['status']}")
        print(f"  Paquetes: {health['total_packages']}")
        print(f"  Duplicados: {health['duplicates']}")

        if health["recommendations"]:
            print("\n💡 Recomendaciones:")
            for rec in health["recommendations"]:
                print(f"  {rec}")

    print("\n✅ Prueba completada")
