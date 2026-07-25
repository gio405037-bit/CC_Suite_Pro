"""
core/database.py
Sistema de base de datos SQLite para CC Suite Pro
"""

import sqlite3
import json
import os
from datetime import datetime
from pathlib import Path
from typing import Dict, List, Optional, Any, Tuple
import logging

logger = logging.getLogger(__name__)


class DatabaseManager:
    """Gestor principal de base de datos"""

    def __init__(self, db_path: str = None):
        """
        Inicializa la conexión a la base de datos

        Args:
            db_path: Ruta del archivo .db (por defecto: data/cc_suite.db)
        """
        if db_path is None:
            # Crear en la carpeta data del proyecto
            base_dir = Path(__file__).parent.parent
            data_dir = base_dir / "data"
            data_dir.mkdir(exist_ok=True)
            db_path = data_dir / "cc_suite.db"

        self.db_path = str(db_path)
        self.conn = None
        self.cursor = None

        # Estadísticas
        self.stats = {"total_packages": 0, "total_size": 0, "last_scan": None}

    # ============================================================
    # CONEXIÓN
    # ============================================================

    def connect(self):
        """Establece conexión con la base de datos"""
        try:
            self.conn = sqlite3.connect(self.db_path, check_same_thread=False)
            self.conn.row_factory = sqlite3.Row  # Acceso por nombre de columna
            self.cursor = self.conn.cursor()

            # Optimizaciones
            self.cursor.execute("PRAGMA journal_mode=WAL")  # Mejor rendimiento
            self.cursor.execute("PRAGMA foreign_keys=ON")  # Activar claves foráneas
            self.cursor.execute("PRAGMA cache_size=-8000")  # 8MB de caché

            logger.info(f"Conectado a base de datos: {self.db_path}")
            return True

        except sqlite3.Error as e:
            logger.error(f"Error conectando a BD: {e}")
            return False

    def disconnect(self):
        """Cierra la conexión a la base de datos"""
        if self.conn:
            self.conn.close()
            logger.info("Conexión a BD cerrada")

    # ============================================================
    # CREACIÓN DE TABLAS
    # ============================================================

    def create_tables(self):
        """Crea todas las tablas necesarias"""

        # Tabla principal de paquetes
        self.cursor.execute("""
            CREATE TABLE IF NOT EXISTS packages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                filename TEXT NOT NULL,
                filepath TEXT NOT NULL UNIQUE,
                size INTEGER NOT NULL,
                extension TEXT NOT NULL,
                
                -- Hash para detectar duplicados
                md5_hash TEXT,
                sha256_hash TEXT,
                
                -- Metadatos
                author TEXT DEFAULT 'Desconocido',
                category TEXT DEFAULT 'Sin categoría',
                tags TEXT DEFAULT '[]',
                
                -- Fechas
                created_date TEXT,
                modified_date TEXT,
                last_scanned TEXT,
                
                -- Estado
                is_active INTEGER DEFAULT 1,
                is_corrupted INTEGER DEFAULT 0,
                is_duplicate INTEGER DEFAULT 0,
                health_score REAL DEFAULT 100.0,
                
                -- Notas del usuario
                user_notes TEXT,
                user_rating INTEGER DEFAULT 0,
                is_favorite INTEGER DEFAULT 0,
                
                -- Información adicional (JSON)
                metadata TEXT DEFAULT '{}'
            )
        """)

        # Historial de escaneos
        self.cursor.execute("""
            CREATE TABLE IF NOT EXISTS scan_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                scan_date TEXT NOT NULL,
                directory TEXT NOT NULL,
                total_files INTEGER DEFAULT 0,
                new_files INTEGER DEFAULT 0,
                updated_files INTEGER DEFAULT 0,
                deleted_files INTEGER DEFAULT 0,
                total_size INTEGER DEFAULT 0,
                duration_seconds REAL DEFAULT 0,
                status TEXT DEFAULT 'completed'
            )
        """)

        # Conflictos detectados
        self.cursor.execute("""
            CREATE TABLE IF NOT EXISTS conflicts (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                package_id_1 INTEGER,
                package_id_2 INTEGER,
                conflict_type TEXT,
                description TEXT,
                detected_date TEXT,
                resolved INTEGER DEFAULT 0,
                FOREIGN KEY(package_id_1) REFERENCES packages(id) ON DELETE CASCADE,
                FOREIGN KEY(package_id_2) REFERENCES packages(id) ON DELETE CASCADE
            )
        """)

        # Dependencias
        self.cursor.execute("""
            CREATE TABLE IF NOT EXISTS dependencies (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                package_id INTEGER,
                requires_package_id INTEGER,
                dependency_type TEXT,
                FOREIGN KEY(package_id) REFERENCES packages(id) ON DELETE CASCADE,
                FOREIGN KEY(requires_package_id) REFERENCES packages(id) ON DELETE CASCADE
            )
        """)

        # Backups
        self.cursor.execute("""
            CREATE TABLE IF NOT EXISTS backups (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                backup_date TEXT NOT NULL,
                path TEXT NOT NULL,
                size INTEGER,
                file_count INTEGER,
                type TEXT DEFAULT 'manual',
                notes TEXT
            )
        """)

        # Configuración
        self.cursor.execute("""
            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT,
                updated_at TEXT
            )
        """)

        self.conn.commit()
        logger.info("Tablas creadas correctamente")

    def create_indexes(self):
        """Crea índices para optimizar búsquedas"""
        indexes = [
            "CREATE INDEX IF NOT EXISTS idx_packages_filename ON packages(filename)",
            "CREATE INDEX IF NOT EXISTS idx_packages_author ON packages(author)",
            "CREATE INDEX IF NOT EXISTS idx_packages_category ON packages(category)",
            "CREATE INDEX IF NOT EXISTS idx_packages_md5 ON packages(md5_hash)",
            "CREATE INDEX IF NOT EXISTS idx_packages_active ON packages(is_active)",
            "CREATE INDEX IF NOT EXISTS idx_packages_favorite ON packages(is_favorite)",
            "CREATE INDEX IF NOT EXISTS idx_scan_date ON scan_history(scan_date)",
        ]

        for index in indexes:
            try:
                self.cursor.execute(index)
            except sqlite3.Error as e:
                logger.warning(f"Error creando índice: {e}")

        self.conn.commit()
        logger.info("Índices creados")

    # ============================================================
    # CRUD - PAQUETES
    # ============================================================

    def insert_package(self, package_data: Dict) -> int:
        """
        Inserta un nuevo paquete en la base de datos

        Args:
            package_data: Diccionario con datos del paquete

        Returns:
            ID del paquete insertado o -1 si error
        """
        try:
            self.cursor.execute(
                """
                INSERT OR REPLACE INTO packages (
                    filename, filepath, size, extension,
                    md5_hash, sha256_hash,
                    author, category, tags,
                    created_date, modified_date, last_scanned,
                    is_active, is_corrupted,
                    metadata
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, datetime('now'), 1, 0, ?)
            """,
                (
                    package_data.get("filename"),
                    package_data.get("filepath"),
                    package_data.get("size", 0),
                    package_data.get("extension", ".package"),
                    package_data.get("md5_hash"),
                    package_data.get("sha256_hash"),
                    package_data.get("author", "Desconocido"),
                    package_data.get("category", "Sin categoría"),
                    json.dumps(package_data.get("tags", [])),
                    package_data.get("created_date"),
                    package_data.get("modified_date"),
                    json.dumps(package_data.get("metadata", {})),
                ),
            )

            self.conn.commit()
            return self.cursor.lastrowid

        except sqlite3.Error as e:
            logger.error(f"Error insertando paquete: {e}")
            return -1

    def get_package_by_id(self, package_id: int) -> Optional[Dict]:
        """Obtiene un paquete por su ID"""
        self.cursor.execute(
            """
            SELECT * FROM packages WHERE id = ?
        """,
            (package_id,),
        )

        row = self.cursor.fetchone()
        return dict(row) if row else None

    def get_package_by_path(self, filepath: str) -> Optional[Dict]:
        """Obtiene un paquete por su ruta"""
        self.cursor.execute(
            """
            SELECT * FROM packages WHERE filepath = ?
        """,
            (filepath,),
        )

        row = self.cursor.fetchone()
        return dict(row) if row else None

    def get_all_packages(
        self,
        category: str = None,
        author: str = None,
        only_favorites: bool = False,
        limit: int = 1000,
    ) -> List[Dict]:
        """
        Obtiene todos los paquetes con filtros opcionales
        """
        query = "SELECT * FROM packages WHERE is_active = 1"
        params = []

        if category:
            query += " AND category = ?"
            params.append(category)

        if author:
            query += " AND author LIKE ?"
            params.append(f"%{author}%")

        if only_favorites:
            query += " AND is_favorite = 1"

        query += " ORDER BY filename LIMIT ?"
        params.append(limit)

        self.cursor.execute(query, params)
        return [dict(row) for row in self.cursor.fetchall()]

    def update_package(self, package_id: int, updates: Dict):
        """Actualiza datos de un paquete"""
        set_clause = ", ".join([f"{k} = ?" for k in updates.keys()])
        values = list(updates.values()) + [package_id]

        self.cursor.execute(
            f"""
            UPDATE packages SET {set_clause} WHERE id = ?
        """,
            values,
        )
        self.conn.commit()

    def delete_package(self, package_id: int, soft_delete: bool = True):
        """
        Elimina un paquete

        Args:
            package_id: ID del paquete
            soft_delete: Si True, solo marca como inactivo
        """
        if soft_delete:
            self.cursor.execute(
                """
                UPDATE packages SET is_active = 0 WHERE id = ?
            """,
                (package_id,),
            )
        else:
            self.cursor.execute(
                """
                DELETE FROM packages WHERE id = ?
            """,
                (package_id,),
            )

        self.conn.commit()

    def toggle_favorite(self, package_id: int) -> bool:
        """Alterna el estado de favorito"""
        self.cursor.execute(
            """
            UPDATE packages SET is_favorite = CASE 
                WHEN is_favorite = 0 THEN 1 
                ELSE 0 
            END
            WHERE id = ?
        """,
            (package_id,),
        )
        self.conn.commit()

        self.cursor.execute(
            "SELECT is_favorite FROM packages WHERE id = ?", (package_id,)
        )
        return bool(self.cursor.fetchone()[0])

    # ============================================================
    # BÚSQUEDAS Y ESTADÍSTICAS
    # ============================================================

    def search_packages(self, query: str) -> List[Dict]:
        """Busca paquetes por nombre, autor o categoría"""
        self.cursor.execute(
            """
            SELECT * FROM packages 
            WHERE is_active = 1 AND (
                filename LIKE ? OR 
                author LIKE ? OR 
                category LIKE ?
            )
            ORDER BY filename
        """,
            (f"%{query}%", f"%{query}%", f"%{query}%"),
        )

        return [dict(row) for row in self.cursor.fetchall()]

    def find_duplicates(self) -> List[Dict]:
        """Encuentra paquetes duplicados por hash MD5"""
        self.cursor.execute("""
            SELECT md5_hash, COUNT(*) as count, 
                   GROUP_CONCAT(filename, ' | ') as files,
                   GROUP_CONCAT(id) as ids
            FROM packages 
            WHERE is_active = 1 AND md5_hash IS NOT NULL
            GROUP BY md5_hash 
            HAVING count > 1
        """)

        return [dict(row) for row in self.cursor.fetchall()]

    def get_statistics(self) -> Dict:
        """Obtiene estadísticas generales"""
        stats = {}

        # Total de paquetes activos
        self.cursor.execute("SELECT COUNT(*) FROM packages WHERE is_active = 1")
        stats["total_packages"] = self.cursor.fetchone()[0]

        # Tamaño total
        self.cursor.execute("SELECT SUM(size) FROM packages WHERE is_active = 1")
        result = self.cursor.fetchone()[0]
        stats["total_size"] = result if result else 0

        # Distribución por categoría
        self.cursor.execute("""
            SELECT category, COUNT(*) as count 
            FROM packages 
            WHERE is_active = 1 
            GROUP BY category 
            ORDER BY count DESC
            LIMIT 10
        """)
        stats["category_distribution"] = [dict(row) for row in self.cursor.fetchall()]

        # Top creadores
        self.cursor.execute("""
            SELECT author, COUNT(*) as count,
                   SUM(size) as total_size
            FROM packages 
            WHERE is_active = 1 AND author != 'Desconocido'
            GROUP BY author 
            ORDER BY count DESC
            LIMIT 10
        """)
        stats["top_creators"] = [dict(row) for row in self.cursor.fetchall()]

        # Paquetes corruptos
        self.cursor.execute("SELECT COUNT(*) FROM packages WHERE is_corrupted = 1")
        stats["corrupted"] = self.cursor.fetchone()[0]

        # Paquetes favoritos
        self.cursor.execute("SELECT COUNT(*) FROM packages WHERE is_favorite = 1")
        stats["favorites"] = self.cursor.fetchone()[0]

        # Último escaneo
        self.cursor.execute("""
            SELECT scan_date, total_files, new_files 
            FROM scan_history 
            ORDER BY scan_date DESC 
            LIMIT 1
        """)
        row = self.cursor.fetchone()
        stats["last_scan"] = dict(row) if row else None

        return stats

    def get_recent_scans(self, limit: int = 5) -> List[Dict]:
        """Obtiene historial de escaneos recientes"""
        self.cursor.execute(
            """
            SELECT * FROM scan_history 
            ORDER BY scan_date DESC 
            LIMIT ?
        """,
            (limit,),
        )

        return [dict(row) for row in self.cursor.fetchall()]

    # ============================================================
    # GESTIÓN DE ESCANEOS
    # ============================================================

    def start_scan(self, directory: str) -> int:
        """Registra el inicio de un escaneo"""
        self.cursor.execute(
            """
            INSERT INTO scan_history (scan_date, directory, status)
            VALUES (datetime('now'), ?, 'in_progress')
        """,
            (directory,),
        )
        self.conn.commit()
        return self.cursor.lastrowid

    def complete_scan(self, scan_id: int, results: Dict):
        """Registra la finalización de un escaneo"""
        self.cursor.execute(
            """
            UPDATE scan_history SET
                total_files = ?,
                new_files = ?,
                updated_files = ?,
                total_size = ?,
                duration_seconds = ?,
                status = 'completed'
            WHERE id = ?
        """,
            (
                results.get("total_files", 0),
                results.get("new_files", 0),
                results.get("updated_files", 0),
                results.get("total_size", 0),
                results.get("duration", 0),
                scan_id,
            ),
        )
        self.conn.commit()

    # ============================================================
    # EXPORTACIÓN
    # ============================================================

    def export_to_json(self, output_path: str):
        """Exporta todos los datos a JSON"""
        data = {
            "packages": self.get_all_packages(limit=99999),
            "statistics": self.get_statistics(),
            "export_date": datetime.now().isoformat(),
        }

        with open(output_path, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2, ensure_ascii=False)

        logger.info(f"Datos exportados a: {output_path}")

    def vacuum(self):
        """Optimiza la base de datos"""
        self.cursor.execute("VACUUM")
        logger.info("Base de datos optimizada (VACUUM)")

    # ============================================================
    # INICIALIZACIÓN
    # ============================================================

    def initialize(self):
        """Inicializa la base de datos completa"""
        if self.connect():
            self.create_tables()
            self.create_indexes()
            self.load_statistics()
            logger.info("Base de datos inicializada correctamente")
            return True
        return False

    def load_statistics(self):
        """Carga estadísticas en memoria"""
        self.stats = self.get_statistics()


# ============================================================
# TEST
# ============================================================

if __name__ == "__main__":
    # Prueba rápida
    print("🧪 Probando DatabaseManager...")

    db = DatabaseManager()
    db.initialize()

    # Insertar paquete de prueba
    test_package = {
        "filename": "test_hair.package",
        "filepath": "C:/Mods/test_hair.package",
        "size": 1048576,
        "extension": ".package",
        "md5_hash": "d41d8cd98f00b204e9800998ecf8427e",
        "author": "Creador Test",
        "category": "Cabello",
        "tags": ["maxis_match", "femenino", "adulto"],
    }

    package_id = db.insert_package(test_package)
    print(f"✅ Paquete insertado con ID: {package_id}")

    # Obtener estadísticas
    stats = db.get_statistics()
    print(f"📊 Paquetes totales: {stats['total_packages']}")
    print(f"💾 Tamaño total: {stats['total_size']} bytes")

    db.disconnect()
    print("✅ Prueba completada")
