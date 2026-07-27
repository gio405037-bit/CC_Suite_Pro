import sys, os

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from PyQt6.QtWidgets import *
from PyQt6.QtCore import *
from PyQt6.QtGui import *
from core.database import DatabaseManager
from core.file_scanner import FileScanner
from core.organizer import OrganizerEngine
from core.duplicate_detector import DuplicateDetector
from utils.config_manager import get_config

C_BG = "#0B0E14"
C_CARD = "#161B26"
C_ACCENT = "#5B4AE0"
C_TEXT = "#E8ECF1"
C_BORDER = "#252B3A"
C_GOLD = "#F0C45A"


class SpinnerWidget(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setFixedSize(80, 80)
        self.angle = 0
        self.timer = QTimer(self)
        self.timer.timeout.connect(self.rotate)
        self.hide()

    def rotate(self):
        self.angle = (self.angle + 30) % 360
        self.update()

    def start(self):
        self.angle = 0
        self.timer.start(80)
        self.show()

    def stop(self):
        self.timer.stop()
        self.hide()

    def paintEvent(self, event):
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        center = self.rect().center()
        radius = 25
        for i in range(12):
            painter.save()
            painter.translate(center)
            painter.rotate(self.angle + i * 30)
            opacity = 1.0 - (i * 0.07)
            color = QColor(C_ACCENT)
            color.setAlpha(int(255 * opacity))
            painter.setPen(Qt.PenStyle.NoPen)
            painter.setBrush(QBrush(color))
            x = radius - 6
            painter.drawRoundedRect(QRect(int(x), -3, 10, 6), 3, 3)
            painter.restore()


class LoadingOverlay(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setAttribute(Qt.WidgetAttribute.WA_TransparentForMouseEvents, False)
        self.hide()
        layout = QVBoxLayout(self)
        layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.spinner = SpinnerWidget()
        layout.addWidget(self.spinner, alignment=Qt.AlignmentFlag.AlignCenter)
        self.label = QLabel("Loading...")
        self.label.setFont(QFont("Segoe UI", 12, QFont.Weight.Bold))
        self.label.setStyleSheet(f"color: {C_TEXT}; background: transparent;")
        self.label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(self.label)
        self.setStyleSheet(f"background-color: rgba(11, 14, 20, 180);")

    def showEvent(self, event):
        self.setGeometry(self.parent().rect())
        self.spinner.start()

    def hideEvent(self, event):
        if hasattr(self, "spinner"):
            self.spinner.stop()

    def set_text(self, text: str):
        self.label.setText(text)


class SettingsDialog(QDialog):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.cfg = get_config()
        self.setWindowTitle(self.cfg.t("settings.title"))
        self.setFixedSize(400, 200)
        self.setStyleSheet(f"""
            QDialog {{ background-color: {C_CARD}; border: 2px solid {C_BORDER}; border-radius: 14px; }}
            QLabel {{ color: {C_TEXT}; background: transparent; font-size: 13px; }}
            QComboBox {{ background-color: {C_BG}; color: {C_TEXT}; border: 1px solid {C_BORDER}; border-radius: 8px; padding: 10px; font-size: 14px; min-width: 200px; }}
            QComboBox:hover {{ border-color: {C_ACCENT}; }}
            QComboBox QAbstractItemView {{ background-color: {C_BG}; color: {C_TEXT}; selection-background-color: {C_ACCENT}; }}
        """)
        layout = QVBoxLayout(self)
        layout.setSpacing(16)
        layout.setContentsMargins(24, 20, 24, 20)
        title = QLabel("⚙️  " + self.cfg.t("settings.title"))
        title.setFont(QFont("Segoe UI", 16, QFont.Weight.Bold))
        title.setStyleSheet(f"color: {C_ACCENT}; background: transparent;")
        layout.addWidget(title)
        row = QHBoxLayout()
        row.addWidget(QLabel(self.cfg.t("settings.language") + ":"))
        row.addStretch()
        self.combo = QComboBox()
        for code, name in self.cfg.LANGUAGES.items():
            self.combo.addItem(name, code)
        for i in range(self.combo.count()):
            if self.combo.itemData(i) == self.cfg.lang:
                self.combo.setCurrentIndex(i)
                break
        row.addWidget(self.combo)
        layout.addLayout(row)
        layout.addStretch()
        btns = QHBoxLayout()
        btns.addStretch()
        save_btn = QPushButton("✅  " + self.cfg.t("settings.save"))
        save_btn.setStyleSheet(
            f"QPushButton {{ background: {C_ACCENT}; color: white; border: none; border-radius: 8px; padding: 10px 20px; font-weight: bold; }} QPushButton:hover {{ background: #7B6EF5; }}"
        )
        save_btn.clicked.connect(self.save)
        btns.addWidget(save_btn)
        close_btn = QPushButton(self.cfg.t("settings.close"))
        close_btn.setStyleSheet(
            f"QPushButton {{ background: {C_BG}; color: {C_TEXT}; border: 1px solid {C_BORDER}; border-radius: 8px; padding: 10px 20px; }} QPushButton:hover {{ border-color: {C_ACCENT}; }}"
        )
        close_btn.clicked.connect(self.reject)
        btns.addWidget(close_btn)
        layout.addLayout(btns)

    def save(self):
        self.cfg.set_language(self.combo.currentData())
        QMessageBox.information(self, "OK", "Language saved! Restart to apply changes.")
        self.accept()


class OrganizeDialog(QDialog):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.cfg = get_config()
        self.t = self.cfg.t
        self.result = {"method": None, "create_backup": True}
        self._build_ui()

    def _build_ui(self):
        self.setWindowTitle("📁  " + self.t("dialogs.organize_title"))
        self.setFixedSize(500, 340)
        self.setStyleSheet(f"""
            QDialog {{ background-color: {C_CARD}; border: 2px solid {C_BORDER}; border-radius: 14px; }}
            QLabel {{ color: {C_TEXT}; background: transparent; }}
            QCheckBox {{ color: {C_TEXT}; background: transparent; font-size: 12px; spacing: 10px; }}
            QCheckBox::indicator {{ width: 24px; height: 24px; border: 2px solid {C_BORDER}; border-radius: 6px; background: {C_BG}; }}
            QCheckBox::indicator:checked {{ background: {C_ACCENT}; border-color: {C_ACCENT}; }}
        """)
        layout = QVBoxLayout(self)
        layout.setSpacing(18)
        layout.setContentsMargins(28, 24, 28, 24)
        title = QLabel("📁  " + self.t("dialogs.organize_title"))
        title.setFont(QFont("Segoe UI", 16, QFont.Weight.Bold))
        title.setStyleSheet(f"color: {C_ACCENT}; background: transparent;")
        layout.addWidget(title)
        question = QLabel(self.t("dialogs.organize_question"))
        question.setFont(QFont("Segoe UI", 12))
        question.setStyleSheet(f"color: {C_TEXT}; background: transparent;")
        layout.addWidget(question)
        layout.addSpacing(10)
        self.backup_checkbox = QCheckBox("💾  " + self.t("dialogs.backup_checkbox"))
        self.backup_checkbox.setChecked(True)
        self.backup_checkbox.setFont(QFont("Segoe UI", 10))
        layout.addWidget(self.backup_checkbox)
        info = QLabel("⚠️  " + self.t("dialogs.backup_info"))
        info.setFont(QFont("Segoe UI", 8))
        info.setStyleSheet(
            "color: #9BA4B5; background: transparent; padding-left: 34px;"
        )
        layout.addWidget(info)
        layout.addStretch()
        sep = QFrame()
        sep.setFrameShape(QFrame.Shape.HLine)
        sep.setStyleSheet(f"background-color: {C_BORDER}; max-height: 1px;")
        layout.addWidget(sep)
        btn_layout = QHBoxLayout()
        btn_layout.setSpacing(10)
        btn_cat = QPushButton("  📁  " + self.t("dialogs.by_category"))
        btn_cat.setMinimumHeight(44)
        btn_cat.setCursor(Qt.CursorShape.PointingHandCursor)
        btn_cat.setFont(QFont("Segoe UI", 10, QFont.Weight.Bold))
        btn_cat.setStyleSheet(
            "QPushButton { background: qlineargradient(x1:0, y1:0, x2:1, y2:0, stop:0 #00B894, stop:1 #55EFC4); color: #0B0E14; border: none; border-radius: 8px; padding: 12px 20px; } QPushButton:hover { background: qlineargradient(x1:0, y1:0, x2:1, y2:0, stop:0 #00D4AA, stop:1 #6FF5D0); }"
        )
        btn_cat.clicked.connect(lambda: self._select("category"))
        btn_layout.addWidget(btn_cat)
        btn_auth = QPushButton("  👤  " + self.t("dialogs.by_author"))
        btn_auth.setMinimumHeight(44)
        btn_auth.setCursor(Qt.CursorShape.PointingHandCursor)
        btn_auth.setFont(QFont("Segoe UI", 10))
        btn_auth.setStyleSheet(
            f"QPushButton {{ background: {C_BG}; color: {C_TEXT}; border: 1px solid {C_BORDER}; border-radius: 8px; padding: 12px 20px; }} QPushButton:hover {{ border-color: {C_ACCENT}; background: #1C2333; }}"
        )
        btn_auth.clicked.connect(lambda: self._select("author"))
        btn_layout.addWidget(btn_auth)
        btn_cancel = QPushButton("  " + self.t("dialogs.cancel"))
        btn_cancel.setMinimumHeight(44)
        btn_cancel.setCursor(Qt.CursorShape.PointingHandCursor)
        btn_cancel.setFont(QFont("Segoe UI", 10))
        btn_cancel.setStyleSheet(
            "QPushButton { background: transparent; color: #9BA4B5; border: 1px solid transparent; border-radius: 8px; padding: 12px 20px; } QPushButton:hover { border-color: #E17055; color: #E17055; }"
        )
        btn_cancel.clicked.connect(self.reject)
        btn_layout.addWidget(btn_cancel)
        layout.addLayout(btn_layout)

    def _select(self, method):
        self.result["method"] = method
        self.result["create_backup"] = self.backup_checkbox.isChecked()
        self.accept()


class ScanWorker(QThread):
    finished = pyqtSignal(dict)

    def __init__(self, db, path):
        super().__init__()
        self.db = db
        self.path = path

    def run(self):
        self.finished.emit(FileScanner(self.db).scan_directory(self.path))


class OrganizeWorker(QThread):
    finished = pyqtSignal(dict)

    def __init__(self, db, path, method, create_backup=True):
        super().__init__()
        self.db = db
        self.path = path
        self.method = method
        self.create_backup = create_backup

    def run(self):
        self.finished.emit(
            OrganizerEngine(self.db).organize(
                self.path, method=self.method, create_backup=self.create_backup
            )
        )


class DuplicateWorker(QThread):
    finished = pyqtSignal(dict)

    def __init__(self, db, path):
        super().__init__()
        self.db = db
        self.path = path

    def run(self):
        self.finished.emit(DuplicateDetector(self.db).find_by_hash(self.path))


class MainWindow(QMainWindow):
    def __init__(self):
        super().__init__()
        self.cfg = get_config()
        self.db = DatabaseManager()
        self.db.initialize()
        self.t = self.cfg.t
        self.setWindowTitle("  CC Suite Pro  |  by Megarorun")
        self.setMinimumSize(1200, 750)
        self.setStyleSheet(f"background-color: {C_BG}; color: {C_TEXT};")
        screen = QApplication.primaryScreen().geometry()
        self.setGeometry(
            (screen.width() - 1200) // 2, (screen.height() - 750) // 2, 1200, 750
        )
        self._build_ui()
        self.refresh()

    def _build_ui(self):
        central = QWidget()
        layout = QVBoxLayout(central)
        layout.setSpacing(15)
        layout.setContentsMargins(30, 20, 30, 20)
        title_layout = QHBoxLayout()
        title = QLabel("🎮  CC Suite Pro")
        title.setFont(QFont("Segoe UI", 24, QFont.Weight.Bold))
        title.setStyleSheet(f"color: {C_ACCENT}; background: transparent;")
        title_layout.addWidget(title)
        title_layout.addStretch()
        brand = QLabel("  by Megarorun")
        brand.setFont(QFont("Segoe UI", 11, QFont.Weight.DemiBold))
        brand.setStyleSheet(
            f"color: {C_GOLD}; background: transparent; padding: 6px 12px; border: 1px solid {C_GOLD}40; border-radius: 8px;"
        )
        title_layout.addWidget(brand)
        layout.addLayout(title_layout)
        subtitle = QLabel(self.t("app.subtitle"))
        subtitle.setFont(QFont("Segoe UI", 10))
        subtitle.setStyleSheet("color: #9BA4B5; background: transparent;")
        layout.addWidget(subtitle)
        self.status_lbl = QLabel(self.t("status.ready"))
        self.status_lbl.setStyleSheet(
            "color: #9BA4B5; padding: 8px 0; background: transparent;"
        )
        layout.addWidget(self.status_lbl)
        btn_row = QHBoxLayout()
        btn_row.setSpacing(10)
        buttons = [
            ("📂  " + self.t("buttons.scan"), "purple", self.scan),
            ("📁  " + self.t("buttons.organize"), "green", self.organize),
            ("🔎  " + self.t("buttons.duplicates"), "orange", self.dupes),
            ("🔄  " + self.t("buttons.refresh"), "yellow", self.refresh),
            ("⚙️  " + self.t("buttons.settings"), "secondary", self.settings),
        ]
        for text, style, func in buttons:
            btn = self._make_button(text, style)
            btn.clicked.connect(func)
            btn_row.addWidget(btn)
        btn_row.addStretch()
        layout.addLayout(btn_row)
        cards = QHBoxLayout()
        cards.setSpacing(12)
        card_info = [
            ("📦  " + self.t("cards.total_packages"), "#7B6EF5", "val_total_packages"),
            ("💾  " + self.t("cards.total_size"), "#55EFC4", "val_total_size"),
            ("👤  " + self.t("cards.creators"), "#FFEAA7", "val_creators"),
            ("🚨  " + self.t("cards.duplicates"), "#F08A76", "val_duplicates"),
        ]
        for title_text, color, obj_name in card_info:
            cards.addWidget(self._make_card(title_text, "0", color, obj_name))
        layout.addLayout(cards)
        header = QHBoxLayout()
        header.addWidget(QLabel("📋  " + self.t("table.title")))
        header.addStretch()
        self.count_lbl = QLabel(self.t("table.no_files"))
        self.count_lbl.setStyleSheet("color: #9BA4B5; background: transparent;")
        header.addWidget(self.count_lbl)
        layout.addLayout(header)
        self.table = QTableWidget()
        self.table.setColumnCount(4)
        self.table.setHorizontalHeaderLabels(
            ["📄 Name", "🏷️ Category", "👤 Author", "💾 Size"]
        )
        self.table.horizontalHeader().setSectionResizeMode(
            0, QHeaderView.ResizeMode.Stretch
        )
        self.table.setSelectionBehavior(QTableWidget.SelectionBehavior.SelectRows)
        self.table.setShowGrid(False)
        self.table.setAlternatingRowColors(True)
        self.table.verticalHeader().setVisible(False)
        self.table.itemClicked.connect(self.show_detail)
        self.table.setStyleSheet(f"""
            QTableWidget {{ background-color: {C_CARD}; color: {C_TEXT}; border: 1px solid {C_BORDER}; border-radius: 10px; font-size: 12px; }}
            QTableWidget::item {{ padding: 10px 14px; border-bottom: 1px solid {C_BORDER}; }}
            QTableWidget::item:selected {{ background-color: {C_ACCENT}40; border-left: 3px solid {C_ACCENT}; }}
            QTableWidget::item:alternate {{ background-color: #11151D; }}
            QHeaderView::section {{ background-color: {C_CARD}; color: #9BA4B5; padding: 10px 14px; border: none; border-bottom: 2px solid {C_BORDER}; font-size: 10px; font-weight: bold; }}
        """)
        layout.addWidget(self.table)
        self.detail_lbl = QLabel("🔍  " + self.t("details.select"))
        self.detail_lbl.setStyleSheet(
            f"color: #9BA4B5; background-color: {C_CARD}; border: 1px solid {C_BORDER}; border-radius: 8px; padding: 12px 16px;"
        )
        self.detail_lbl.setWordWrap(True)
        self.detail_lbl.setMinimumHeight(50)
        layout.addWidget(self.detail_lbl)
        self.setCentralWidget(central)
        self.loading = LoadingOverlay(central)
        self.statusBar().setStyleSheet(
            f"background-color: {C_ACCENT}; color: white; font-weight: bold; padding: 4px;"
        )
        self.statusBar().showMessage(
            "✅  CC Suite Pro  |  by Megarorun  |  " + self.cfg.get_language_name()
        )

    def _make_button(self, text, style):
        btn = QPushButton("  " + text)
        btn.setMinimumHeight(44)
        btn.setCursor(Qt.CursorShape.PointingHandCursor)
        btn.setFont(QFont("Segoe UI", 10, QFont.Weight.Bold))
        styles = {
            "purple": f"QPushButton {{ background: qlineargradient(x1:0, y1:0, x2:1, y2:0, stop:0 #5B4AE0, stop:1 #7B6EF5); color: white; border: none; border-radius: 10px; padding: 10px 20px; }} QPushButton:hover {{ background: qlineargradient(x1:0, y1:0, x2:1, y2:0, stop:0 #6B5EF0, stop:1 #8B80FF); }}",
            "green": f"QPushButton {{ background: qlineargradient(x1:0, y1:0, x2:1, y2:0, stop:0 #00B894, stop:1 #55EFC4); color: #0B0E14; border: none; border-radius: 10px; padding: 10px 20px; }} QPushButton:hover {{ background: qlineargradient(x1:0, y1:0, x2:1, y2:0, stop:0 #00D4AA, stop:1 #6FF5D0); }}",
            "orange": f"QPushButton {{ background-color: #E17055; color: white; border: none; border-radius: 10px; padding: 10px 20px; }} QPushButton:hover {{ background-color: #F08A76; }}",
            "yellow": f"QPushButton {{ background-color: #FDCB6E; color: #0B0E14; border: none; border-radius: 10px; padding: 10px 20px; }} QPushButton:hover {{ background-color: #FFEAA7; }}",
            "secondary": f"QPushButton {{ background-color: {C_CARD}; color: {C_TEXT}; border: 1px solid {C_BORDER}; border-radius: 10px; padding: 10px 20px; }} QPushButton:hover {{ border-color: {C_ACCENT}; }}",
        }
        btn.setStyleSheet(styles.get(style, styles["secondary"]))
        return btn

    def _make_card(self, title, value, color, obj_name):
        card = QFrame()
        card.setStyleSheet(
            f"QFrame {{ background-color: {C_CARD}; border: 1px solid {C_BORDER}; border-radius: 12px; padding: 16px; }} QFrame:hover {{ border-color: {color}60; }}"
        )
        layout = QVBoxLayout(card)
        layout.setSpacing(6)
        val_lbl = QLabel(value)
        val_lbl.setFont(QFont("Segoe UI", 20, QFont.Weight.Bold))
        val_lbl.setStyleSheet(f"color: {color}; background: transparent;")
        val_lbl.setObjectName(obj_name)
        layout.addWidget(val_lbl)
        title_lbl = QLabel(title.upper())
        title_lbl.setFont(QFont("Segoe UI", 8, QFont.Weight.Bold))
        title_lbl.setStyleSheet(
            "color: #9BA4B5; letter-spacing: 1px; background: transparent;"
        )
        layout.addWidget(title_lbl)
        return card

    def _show_loading(self, text: str):
        self.loading.set_text(text)
        self.loading.setGeometry(self.centralWidget().rect())
        self.loading.show()
        self.loading.raise_()
        QApplication.processEvents()

    def _hide_loading(self):
        self.loading.hide()
        QApplication.processEvents()

    def settings(self):
        SettingsDialog(self).exec()
        self.statusBar().showMessage(
            "✅  CC Suite Pro  |  by Megarorun  |  " + self.cfg.get_language_name()
        )

    def scan(self):
        path = QFileDialog.getExistingDirectory(
            self,
            "📂 Select Mods Folder",
            os.path.expanduser("~/Documents/Electronic Arts/The Sims 4/Mods"),
        )
        if not path:
            return
        self._show_loading("📂 Scanning files...")
        self.status_lbl.setText("🔍 " + self.t("status.scanning"))
        self.scan_worker = ScanWorker(self.db, path)
        self.scan_worker.finished.connect(self._on_scan_done)
        self.scan_worker.start()

    def _on_scan_done(self, result: dict):
        self._hide_loading()
        self.status_lbl.setText("✅ " + self.t("status.scan_done"))
        QMessageBox.information(
            self,
            "✅ " + self.t("dialogs.scan_complete"),
            f"📁 Files: {result['total_files']}\n💾 Size: {result.get('total_size', 0) / (1024*1024):.1f} MB\n⏱️ Time: {result.get('duration', 0)}s",
        )
        self.refresh()

    def organize(self):
        path = QFileDialog.getExistingDirectory(
            self,
            "📁 Select Folder",
            os.path.expanduser("~/Documents/Electronic Arts/The Sims 4/Mods"),
        )
        if not path:
            return
        dialog = OrganizeDialog(self)
        dialog.exec()
        method = dialog.result["method"]
        create_backup = dialog.result["create_backup"]
        if method is None:
            return
        if not create_backup:
            confirm = QMessageBox.warning(
                self,
                "⚠️ " + self.t("dialogs.no_backup_title"),
                self.t("dialogs.no_backup_warning"),
                QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
            )
            if confirm != QMessageBox.StandardButton.Yes:
                return
        confirm = QMessageBox.question(
            self,
            "⚠️ " + self.t("dialogs.confirm"),
            self.t("dialogs.confirm_organize", method=method)
            + "\n\n"
            + (
                "📦 " + self.t("dialogs.backup_yes") + " ✅"
                if create_backup
                else "⚠️ " + self.t("dialogs.backup_no") + " ❌"
            ),
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
        )
        if confirm == QMessageBox.StandardButton.Yes:
            self._show_loading("📁 Organizing files...")
            self.status_lbl.setText("📁 " + self.t("status.organizing"))
            self.org_worker = OrganizeWorker(self.db, path, method, create_backup)
            self.org_worker.finished.connect(self._on_organize_done)
            self.org_worker.start()

    def _on_organize_done(self, result: dict):
        self._hide_loading()
        self.status_lbl.setText("✅ " + self.t("status.organize_done"))
        QMessageBox.information(
            self,
            "✅ OK",
            f"📁 Total: {result['total_files']}\n✅ Moved: {result['moved']}\n❌ Errors: {result['errors']}",
        )
        self.refresh()

    def dupes(self):
        path = QFileDialog.getExistingDirectory(
            self,
            "🔎 Select Folder",
            os.path.expanduser("~/Documents/Electronic Arts/The Sims 4/Mods"),
        )
        if not path:
            return
        self._show_loading("🔎 Detecting duplicates...")
        self.status_lbl.setText("🔎 " + self.t("status.detecting"))
        self.dupe_worker = DuplicateWorker(self.db, path)
        self.dupe_worker.finished.connect(self._on_dupes_done)
        self.dupe_worker.start()

    def _on_dupes_done(self, result: dict):
        self._hide_loading()
        self.status_lbl.setText(
            "✅ " + self.t("status.detect_done") + f" - {result['duplicates_found']}"
        )
        if result["duplicates_found"] > 0:
            reply = QMessageBox.question(
                self,
                "🚨 " + self.t("dialogs.duplicates_found"),
                f"📁 Scanned: {result['total_scanned']}\n🚨 Duplicates: {result['duplicates_found']}\n💾 Wasted: {result['wasted_space_formatted']}\n\n{self.t('dialogs.delete_duplicates')}",
                QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
            )
            if reply == QMessageBox.StandardButton.Yes:
                confirm = QMessageBox.warning(
                    self,
                    "⚠️ " + self.t("dialogs.warning_delete"),
                    self.t("dialogs.warning_delete_msg"),
                    QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
                )
                if confirm == QMessageBox.StandardButton.Yes:
                    self._show_loading("🗑️ Deleting duplicates...")
                    all_dupes = []
                    for group in result["groups"]:
                        all_dupes.extend(group["items"])
                    delete_result = DuplicateDetector(self.db).delete_duplicates(
                        all_dupes, dry_run=False
                    )
                    self._hide_loading()
                    QMessageBox.information(
                        self,
                        "✅ " + self.t("dialogs.deleted"),
                        f"🗑️ Deleted: {delete_result['deleted']}\n💾 Freed: {delete_result['freed_space_formatted']}",
                    )
                    self.refresh()
        else:
            QMessageBox.information(
                self,
                "✅ " + self.t("dialogs.no_duplicates"),
                self.t("dialogs.no_duplicates_msg"),
            )

    def show_detail(self, item: QTableWidgetItem):
        row = item.row()
        self.detail_lbl.setText(
            f"📄 {self.table.item(row, 0).text()} | 🏷️ {self.table.item(row, 1).text()} | 👤 {self.table.item(row, 2).text()} | 💾 {self.table.item(row, 3).text()}"
        )

    def refresh(self):
        stats = self.db.get_statistics()
        self._update_card("val_total_packages", str(stats["total_packages"]))
        size = stats["total_size"]
        self._update_card(
            "val_total_size",
            (
                f"{size / 1024**3:.1f} GB"
                if size > 1024**3
                else (
                    f"{size / 1024**2:.1f} MB"
                    if size > 1024**2
                    else f"{size / 1024:.1f} KB"
                )
            ),
        )
        self._update_card("val_creators", str(len(stats.get("top_creators", []))))
        self._update_card("val_duplicates", str(len(self.db.find_duplicates())))
        self.table.setRowCount(0)
        for pkg in self.db.get_all_packages(limit=100):
            row = self.table.rowCount()
            self.table.insertRow(row)
            self.table.setItem(row, 0, QTableWidgetItem(pkg.get("filename", "")))
            self.table.setItem(row, 1, QTableWidgetItem(pkg.get("category", "Unknown")))
            self.table.setItem(row, 2, QTableWidgetItem(pkg.get("author", "Unknown")))
            self.table.setItem(
                row, 3, QTableWidgetItem(f"{pkg.get('size', 0) / 1024:.1f} KB")
            )
        self.count_lbl.setText(f"{stats['total_packages']} files")

    def _update_card(self, obj_name, value):
        widget = self.findChild(QLabel, obj_name)
        if widget:
            widget.setText(value)


def main():
    app = QApplication(sys.argv)
    app.setFont(QFont("Segoe UI", 10))
    app.setApplicationName("CC Suite Pro")
    icon_path = os.path.join(os.path.dirname(__file__), "icon.png")
    if os.path.exists(icon_path):
        app.setWindowIcon(QIcon(icon_path))
    window = MainWindow()
    window.show()
    sys.exit(app.exec())


if __name__ == "__main__":
    main()
