def dupes(self):
    """Detecta archivos duplicados"""
    path = QFileDialog.getExistingDirectory(
        self,
        "Select Folder",
        os.path.expanduser("~/Documents/Electronic Arts/The Sims 4/Mods"),
    )

    if not path:
        return

    self.status_lbl.setText(self.t("status.detecting"))
    QApplication.processEvents()

    detector = DuplicateDetector(self.db)
    result = detector.find_by_hash(path)

    self.status_lbl.setText(
        self.t("status.detect_done") + f" - {result['duplicates_found']}"
    )

    if result["duplicates_found"] > 0:
        reply = QMessageBox.question(
            self,
            self.t("dialogs.duplicates_found"),
            f"Scanned: {result['total_scanned']}\n"
            f"Duplicates: {result['duplicates_found']}\n"
            f"Wasted: {result['wasted_space_formatted']}\n\n"
            f"{self.t('dialogs.delete_duplicates')}",
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
        )

        if reply == QMessageBox.StandardButton.Yes:
            confirm = QMessageBox.warning(
                self,
                self.t("dialogs.warning_delete"),
                self.t("dialogs.warning_delete_msg"),
                QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
            )

            if confirm == QMessageBox.StandardButton.Yes:
                all_dupes = []
                for group in result["groups"]:
                    all_dupes.extend(group["items"])

                delete_result = detector.delete_duplicates(all_dupes, dry_run=False)

                QMessageBox.information(
                    self,
                    self.t("dialogs.deleted"),
                    f"Deleted: {delete_result['deleted']}\n"
                    f"Freed: {delete_result['freed_space_formatted']}",
                )
                self.refresh()
    else:
        QMessageBox.information(
            self, self.t("dialogs.no_duplicates"), self.t("dialogs.no_duplicates_msg")
        )
